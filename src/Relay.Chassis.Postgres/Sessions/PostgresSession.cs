using Npgsql;

namespace Relay.Chassis.Postgres.Sessions;

public sealed class PostgresSession(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    private NpgsqlConnection? connection;
    private NpgsqlTransaction? transaction;

    public async ValueTask<NpgsqlTransaction> Transaction(CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            return transaction;
        }

        connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        return transaction;
    }

    public async Task Commit(CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            return;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await Release().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await Release().ConfigureAwait(false);

    private async ValueTask Release()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            transaction = null;
        }

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            connection = null;
        }
    }
}
