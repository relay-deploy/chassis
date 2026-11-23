package events

import (
	"errors"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
)

const (
	outboxTable       = "outbox_events"
	cursorTable       = "event_cursors"
	undefinedTable    = "42P01"
	invalidSchemaName = "3F000"
)

func eventChannel(source string) string {
	return "relay_events_" + source
}

func readCommittedSQL(source string) string {
	return `SELECT sequence, id, type, subject_id, payload::text, occurred_at, transaction_id
FROM ` + pgx.Identifier{source, outboxTable}.Sanitize() + `
WHERE (transaction_id, sequence) > ($1, $2)
  AND transaction_id < (pg_snapshot_xmin(pg_current_snapshot())::text)::bigint
ORDER BY transaction_id, sequence
LIMIT $3`
}

func cursorSQL(consumer string) string {
	return `SELECT transaction_id, sequence FROM ` + pgx.Identifier{consumer, cursorTable}.Sanitize() + ` WHERE source = $1`
}

func advanceSQL(consumer string) string {
	return `INSERT INTO ` + pgx.Identifier{consumer, cursorTable}.Sanitize() + ` AS stored (source, transaction_id, sequence)
VALUES ($1, $2, $3)
ON CONFLICT (source) DO UPDATE SET transaction_id = excluded.transaction_id, sequence = excluded.sequence
WHERE (stored.transaction_id, stored.sequence) < (excluded.transaction_id, excluded.sequence)`
}

func isSourceMissing(err error) bool {
	var pgError *pgconn.PgError
	return errors.As(err, &pgError) && (pgError.Code == undefinedTable || pgError.Code == invalidSchemaName)
}
