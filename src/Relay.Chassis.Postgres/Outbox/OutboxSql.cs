namespace Relay.Chassis.Postgres.Outbox;

internal static class OutboxSql
{
    public const string Table = "outbox_events";

    public const string CurrentTransactionId = "(pg_current_xact_id()::text)::bigint";

    public const string Notify = "SELECT pg_notify({0}, '')";

    public static string Listen(PostgresSchema schema) => $"LISTEN {schema.EventChannel}";

    public static string ReadCommitted(PostgresSchema source) => $"""
        SELECT sequence, id, type, subject_id, payload::text, occurred_at, transaction_id
        FROM {source.Name}.{Table}
        WHERE (transaction_id, sequence) > (@transactionId, @sequence)
          AND transaction_id < (pg_snapshot_xmin(pg_current_snapshot())::text)::bigint
        ORDER BY transaction_id, sequence
        LIMIT @limit
        """;
}
