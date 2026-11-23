using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Relay.Chassis.Postgres.Outbox;
using Relay.Chassis.Primitives;

namespace Relay.Chassis.Postgres.Events;

public sealed partial class EventSubscription<TContext>(
    PostgresSchema source,
    IServiceScopeFactory scopes,
    IEnumerable<EventConsumerRegistration> registrations,
    IOptions<PostgresOptions> postgres,
    IOptions<EventSubscriptionOptions> options,
    TimeProvider time,
    ILogger<EventSubscription<TContext>> logger) : BackgroundService
    where TContext : DbContext, IEventCursorContext
{
    private readonly Dictionary<string, EventConsumerRegistration> handlers =
        registrations.Where(registration => registration.Source == source.Name).ToDictionary(registration => registration.Type);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(postgres.Value.ConnectionString);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Listen(dataSource, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (PostgresFailures.IsUnavailable(exception) || IsSourceMissing(exception))
            {
                LogSourceUnavailable(source.Name, exception.Message);
                await Task.Delay(options.Value.RetryDelay, time, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsSourceMissing(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName };

    private async Task Listen(NpgsqlDataSource dataSource, CancellationToken stoppingToken)
    {
        await using var listener = await dataSource.OpenConnectionAsync(stoppingToken).ConfigureAwait(false);
        await using (var listen = listener.CreateCommand())
        {
            listen.CommandText = OutboxSql.Listen(source);
            await listen.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (await Drain(dataSource, stoppingToken).ConfigureAwait(false))
            {
                await listener.WaitAsync(options.Value.FallbackPoll, stoppingToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(options.Value.RetryDelay, time, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> Drain(NpgsqlDataSource dataSource, CancellationToken stoppingToken)
    {
        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            var cursor = await CursorOf(context, stoppingToken).ConfigureAwait(false);
            var batch = await Read(dataSource, cursor, stoppingToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return true;
            }

            foreach (var committed in batch)
            {
                if (!await Dispatch(committed, stoppingToken).ConfigureAwait(false))
                {
                    return false;
                }

                cursor.TransactionId = committed.TransactionId;
                cursor.Sequence = committed.Sequence;
                await context.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<EventCursor> CursorOf(TContext context, CancellationToken stoppingToken)
    {
        var cursor = await context.EventCursors.FindAsync([source.Name], stoppingToken).ConfigureAwait(false);
        if (cursor is not null)
        {
            return cursor;
        }

        var created = new EventCursor { Source = source.Name };
        context.EventCursors.Add(created);
        return created;
    }

    private async Task<List<CommittedEvent>> Read(NpgsqlDataSource dataSource, EventCursor cursor, CancellationToken stoppingToken)
    {
        await using var command = dataSource.CreateCommand(OutboxSql.ReadCommitted(source));
        command.Parameters.AddWithValue("transactionId", cursor.TransactionId);
        command.Parameters.AddWithValue("sequence", cursor.Sequence);
        command.Parameters.AddWithValue("limit", options.Value.BatchSize);
        await using var reader = await command.ExecuteReaderAsync(stoppingToken).ConfigureAwait(false);
        var events = new List<CommittedEvent>();
        while (await reader.ReadAsync(stoppingToken).ConfigureAwait(false))
        {
            var subject = await reader.IsDBNullAsync(3, stoppingToken).ConfigureAwait(false) ? (Guid?)null : reader.GetGuid(3);
            events.Add(new CommittedEvent(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetString(2),
                subject,
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetInt64(6)));
        }

        return events;
    }

    private async Task<bool> Dispatch(CommittedEvent committed, CancellationToken stoppingToken)
    {
        if (!handlers.TryGetValue(committed.Type, out var registration))
        {
            return true;
        }

        await using var scope = scopes.CreateAsyncScope();
        try
        {
            var result = await registration.Run(scope.ServiceProvider, committed.Payload, committed.Envelope, stoppingToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                return true;
            }

            if (result.Error.Kind == ErrorKind.Unavailable)
            {
                LogHandlerDeferred(committed.Type, committed.Id, result.Error.Code);
                return false;
            }

            LogEventRejected(committed.Type, committed.Id, result.Error.Code, result.Error.Message);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogHandlerThrew(exception, committed.Type, committed.Id);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Events from {Source} are unavailable ({Reason}), retrying")]
    private partial void LogSourceUnavailable(string source, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handler for {Type} deferred event {EventId} ({Code}), retrying")]
    private partial void LogHandlerDeferred(string type, Guid eventId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handler for {Type} rejected event {EventId} ({Code}: {Reason}), skipping it")]
    private partial void LogEventRejected(string type, Guid eventId, string code, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handler for {Type} threw on event {EventId}, retrying")]
    private partial void LogHandlerThrew(Exception exception, string type, Guid eventId);
}
