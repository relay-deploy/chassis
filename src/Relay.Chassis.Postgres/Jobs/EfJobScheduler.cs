using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Relay.Chassis.Postgres.Jobs;

public sealed class EfJobScheduler<TContext>(TContext context, IOptions<JobOptions> options, TimeProvider time) : IJobScheduler
    where TContext : DbContext, IJobContext
{
    public void Schedule<TJob>(TJob job, string idempotencyKey)
        where TJob : IJob
    {
        var now = time.GetUtcNow();
        context.Jobs.Add(new JobRecord
        {
            Id = Guid.CreateVersion7(),
            Kind = TJob.Kind,
            Payload = JsonSerializer.Serialize(job, JobSerialization.Options),
            IdempotencyKey = idempotencyKey,
            RunAt = now,
            MaxAttempts = options.Value.MaxAttempts,
            CreatedAt = now,
        });
    }
}
