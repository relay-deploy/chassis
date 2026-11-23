using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Relay.Chassis.Primitives;

namespace Relay.Chassis.Postgres.Jobs;

public sealed partial class JobWorker(
    NpgsqlDataSource dataSource,
    IEnumerable<JobRegistration> registrations,
    IServiceScopeFactory scopes,
    IOptions<JobOptions> options,
    TimeProvider time,
    ILogger<JobWorker> logger) : BackgroundService
{
    private const string ClaimSql = """
        UPDATE job SET locked_until = @now + @lease, attempts = attempts + 1
        WHERE id = (
            SELECT id FROM job
            WHERE failed_at IS NULL AND run_at <= @now AND (locked_until IS NULL OR locked_until < @now)
            ORDER BY run_at
            FOR UPDATE SKIP LOCKED
            LIMIT 1)
        RETURNING id, kind, payload::text AS payload, attempts, max_attempts
        """;

    private const string CompleteSql = "DELETE FROM job WHERE id = @id";

    private const string RetrySql = "UPDATE job SET run_at = @runAt, locked_until = NULL, last_error = @error WHERE id = @id";

    private const string FailSql = "UPDATE job SET failed_at = @now, locked_until = NULL, last_error = @error WHERE id = @id";

    private readonly Dictionary<string, JobRegistration> handlers = registrations.ToDictionary(registration => registration.Kind);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, options.Value.Concurrency).Select(_ => Poll(stoppingToken)));

    private async Task Poll(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = await TryRunNext(stoppingToken).ConfigureAwait(false);
            if (!worked)
            {
                await Task.Delay(options.Value.PollInterval, time, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> TryRunNext(CancellationToken stoppingToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(stoppingToken).ConfigureAwait(false);
            var job = await connection.QuerySingleOrDefaultAsync<ClaimedJob>(new CommandDefinition(
                ClaimSql, new { now = time.GetUtcNow(), lease = options.Value.Lease }, cancellationToken: stoppingToken)).ConfigureAwait(false);
            if (job is null)
            {
                return false;
            }

            var outcome = await Run(job, stoppingToken).ConfigureAwait(false);
            await Settle(connection, job, outcome, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (NpgsqlException exception)
        {
            LogDatabaseUnavailable(exception.Message);
            return false;
        }
    }

    private async Task<Result> Run(ClaimedJob job, CancellationToken stoppingToken)
    {
        if (!handlers.TryGetValue(job.Kind, out var registration))
        {
            return Error.Validation("job.unknown_kind", $"No handler is registered for job kind {job.Kind}.");
        }

        await using var scope = scopes.CreateAsyncScope();
        try
        {
            return await registration.Run(scope.ServiceProvider, job.Payload, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogJobThrew(exception, job.Kind, job.Id);
            return Error.Unavailable("job.threw", exception.Message);
        }
    }

    private async Task Settle(NpgsqlConnection connection, ClaimedJob job, Result outcome, CancellationToken stoppingToken)
    {
        if (outcome.IsSuccess)
        {
            await connection.ExecuteAsync(new CommandDefinition(CompleteSql, new { id = job.Id }, cancellationToken: stoppingToken)).ConfigureAwait(false);
            return;
        }

        var error = $"{outcome.Error.Code}: {outcome.Error.Message}";
        var retryable = outcome.Error.Kind == ErrorKind.Unavailable && job.Attempts < job.MaxAttempts;
        if (retryable)
        {
            var runAt = time.GetUtcNow() + Backoff(job.Attempts);
            await connection.ExecuteAsync(new CommandDefinition(RetrySql, new { id = job.Id, runAt, error }, cancellationToken: stoppingToken)).ConfigureAwait(false);
            LogJobRetrying(job.Kind, job.Id, job.Attempts, error, runAt);
            return;
        }

        await connection.ExecuteAsync(new CommandDefinition(FailSql, new { id = job.Id, now = time.GetUtcNow(), error }, cancellationToken: stoppingToken)).ConfigureAwait(false);
        LogJobFailed(job.Kind, job.Id, job.Attempts, error);
    }

    private TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempts), options.Value.MaxBackoff.TotalSeconds));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job queue cannot reach the database: {Reason}")]
    private partial void LogDatabaseUnavailable(string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Kind} {JobId} threw")]
    private partial void LogJobThrew(Exception exception, string kind, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {Kind} {JobId} attempt {Attempt} failed ({Error}), retrying at {RunAt}")]
    private partial void LogJobRetrying(string kind, Guid jobId, int attempt, string error, DateTimeOffset runAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Kind} {JobId} failed for good after {Attempt} attempts ({Error})")]
    private partial void LogJobFailed(string kind, Guid jobId, int attempt, string error);
}
