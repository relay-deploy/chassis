using Dapper;
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

    public async Task<int> Execute(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var current = await Transaction(cancellationToken).ConfigureAwait(false);
        return await current.Connection!.ExecuteAsync(Command(sql, parameters, current, cancellationToken)).ConfigureAwait(false);
    }

    public async Task<T?> QuerySingleOrDefault<T>(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var current = await Transaction(cancellationToken).ConfigureAwait(false);
        return await current.Connection!.QuerySingleOrDefaultAsync<T>(Command(sql, parameters, current, cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> Query<T>(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var current = await Transaction(cancellationToken).ConfigureAwait(false);
        var rows = await current.Connection!.QueryAsync<T>(Command(sql, parameters, current, cancellationToken)).ConfigureAwait(false);
        return rows.AsList();
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

    private static CommandDefinition Command(string sql, object? parameters, NpgsqlTransaction transaction, CancellationToken cancellationToken) =>
        new(sql, parameters, transaction, cancellationToken: cancellationToken);

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
