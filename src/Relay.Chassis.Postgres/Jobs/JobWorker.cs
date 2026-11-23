using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relay.Chassis.Primitives;

namespace Relay.Chassis.Postgres.Jobs;

public sealed partial class JobWorker<TContext>(
    IServiceScopeFactory scopes,
    IEnumerable<JobRegistration> registrations,
    IOptions<JobOptions> options,
    TimeProvider time,
    ILogger<JobWorker<TContext>> logger) : BackgroundService
    where TContext : DbContext, IJobContext
{
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
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            var job = await Claim(context, stoppingToken).ConfigureAwait(false);
            if (job is null)
            {
                return false;
            }

            var attempt = new JobAttempt(job.Attempts + 1, job.MaxAttempts);
            var outcome = await Run(job, attempt, stoppingToken).ConfigureAwait(false);
            await Settle(context, job, attempt, outcome, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (PostgresFailures.IsUnavailable(exception))
        {
            LogDatabaseUnavailable(exception.Message);
            return false;
        }
    }

    private async Task<JobRecord?> Claim(TContext context, CancellationToken stoppingToken)
    {
        var now = time.GetUtcNow();
        var candidate = await context.Jobs
            .AsNoTracking()
            .Where(job => job.FailedAt == null && job.RunAt <= now && (job.LockedUntil == null || job.LockedUntil < now))
            .OrderBy(job => job.RunAt)
            .FirstOrDefaultAsync(stoppingToken)
            .ConfigureAwait(false);
        if (candidate is null)
        {
            return null;
        }

        var lockedUntil = now + options.Value.Lease;
        var claimed = await context.Jobs
            .Where(job => job.Id == candidate.Id && job.Attempts == candidate.Attempts && (job.LockedUntil == null || job.LockedUntil < now))
            .ExecuteUpdateAsync(
                job => job
                    .SetProperty(record => record.LockedUntil, lockedUntil)
                    .SetProperty(record => record.Attempts, record => record.Attempts + 1),
                stoppingToken)
            .ConfigureAwait(false);
        return claimed == 1 ? candidate : null;
    }

    private async Task<Result> Run(JobRecord job, JobAttempt attempt, CancellationToken stoppingToken)
    {
        if (!handlers.TryGetValue(job.Kind, out var registration))
        {
            return Error.Validation("job.unknown_kind", $"No handler is registered for job kind {job.Kind}.");
        }

        await using var scope = scopes.CreateAsyncScope();
        try
        {
            return await registration.Run(scope.ServiceProvider, job.Payload, attempt, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogJobThrew(exception, job.Kind, job.Id);
            return Error.Unavailable("job.threw", exception.Message);
        }
    }

    private async Task Settle(TContext context, JobRecord job, JobAttempt attempt, Result outcome, CancellationToken stoppingToken)
    {
        var jobs = context.Jobs.Where(record => record.Id == job.Id);
        if (outcome.IsSuccess)
        {
            await jobs.ExecuteDeleteAsync(stoppingToken).ConfigureAwait(false);
            return;
        }

        var error = $"{outcome.Error.Code}: {outcome.Error.Message}";
        if (outcome.Error.Kind == ErrorKind.Unavailable && !attempt.IsLast)
        {
            var runAt = time.GetUtcNow() + Backoff(attempt.Number);
            await jobs.ExecuteUpdateAsync(
                record => record
                    .SetProperty(column => column.RunAt, runAt)
                    .SetProperty(column => column.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(column => column.LastError, error),
                stoppingToken).ConfigureAwait(false);
            LogJobRetrying(job.Kind, job.Id, attempt.Number, error, runAt);
            return;
        }

        var failedAt = time.GetUtcNow();
        await jobs.ExecuteUpdateAsync(
            record => record
                .SetProperty(column => column.FailedAt, failedAt)
                .SetProperty(column => column.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(column => column.LastError, error),
            stoppingToken).ConfigureAwait(false);
        LogJobFailed(job.Kind, job.Id, attempt.Number, error);
    }

    private TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), options.Value.MaxBackoff.TotalSeconds));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job queue cannot reach the database: {Reason}")]
    private partial void LogDatabaseUnavailable(string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Kind} {JobId} threw")]
    private partial void LogJobThrew(Exception exception, string kind, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {Kind} {JobId} attempt {Attempt} failed ({Error}), retrying at {RunAt}")]
    private partial void LogJobRetrying(string kind, Guid jobId, int attempt, string error, DateTimeOffset runAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Kind} {JobId} failed for good after {Attempt} attempts ({Error})")]
    private partial void LogJobFailed(string kind, Guid jobId, int attempt, string error);
}
