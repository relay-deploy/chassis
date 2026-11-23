using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Relay.Chassis.Postgres.Outbox;

public sealed class OutboxDoorbell(PostgresSchema schema) : SaveChangesInterceptor
{
    private bool eventsPending;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        eventsPending = eventData.Context?.ChangeTracker.Entries<OutboxEvent>().Any(entry => entry.State == EntityState.Added) ?? false;
        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventsPending && eventData.Context is { } context)
        {
            eventsPending = false;
            await context.Database.ExecuteSqlRawAsync(OutboxSql.Notify, [schema.EventChannel], cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
