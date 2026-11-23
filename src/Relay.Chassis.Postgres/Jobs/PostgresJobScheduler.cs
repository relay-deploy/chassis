using System.Text.Json;
using Microsoft.Extensions.Options;
using Relay.Chassis.Postgres.Sessions;

namespace Relay.Chassis.Postgres.Jobs;

public sealed class PostgresJobScheduler(PostgresSession session, IOptions<JobOptions> options, TimeProvider time) : IJobScheduler
{
    private const string ScheduleSql = """
        INSERT INTO job (id, kind, payload, idempotency_key, run_at, attempts, max_attempts, created_at)
        VALUES (@id, @kind, @payload::jsonb, @idempotencyKey, @now, 0, @maxAttempts, @now)
        ON CONFLICT (idempotency_key) DO NOTHING
        """;

    public async Task Schedule<TJob>(TJob job, string idempotencyKey, CancellationToken cancellationToken)
        where TJob : IJob
    {
        var parameters = new
        {
            id = Guid.CreateVersion7(),
            kind = TJob.Kind,
            payload = JsonSerializer.Serialize(job, JobSerialization.Options),
            idempotencyKey,
            now = time.GetUtcNow(),
            maxAttempts = options.Value.MaxAttempts,
        };
        await session.Execute(ScheduleSql, parameters, cancellationToken).ConfigureAwait(false);
    }
}
