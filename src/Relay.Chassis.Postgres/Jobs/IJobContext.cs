using Microsoft.EntityFrameworkCore;

namespace Relay.Chassis.Postgres.Jobs;

public interface IJobContext
{
    DbSet<JobRecord> Jobs { get; }
}
