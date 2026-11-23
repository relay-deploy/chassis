using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Relay.Chassis.Postgres;

public sealed partial class DatabaseMigrator<TContext>(IServiceScopeFactory scopes, TimeProvider time, ILogger<DatabaseMigrator<TContext>> logger)
    : IHostedService
    where TContext : DbContext
{
    private static readonly TimeSpan maxRetryDelay = TimeSpan.FromSeconds(30);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (true)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                LogMigrated(typeof(TContext).Name);
                return;
            }
            catch (Exception exception) when (PostgresFailures.IsUnavailable(exception))
            {
                LogDatabaseUnavailable(exception.Message, delay);
                await Task.Delay(delay, time, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, maxRetryDelay.Ticks));
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Database for {Context} is migrated")]
    private partial void LogMigrated(string context);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database unavailable ({Reason}), retrying migrations in {Delay}")]
    private partial void LogDatabaseUnavailable(string reason, TimeSpan delay);
}
