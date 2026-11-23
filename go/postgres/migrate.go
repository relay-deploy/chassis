package postgres

import (
	"context"
	"fmt"
	"io/fs"
	"sort"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/relay-deploy/chassis/go/logging"
)

const (
	initialRetryDelay = time.Second
	maxRetryDelay     = 30 * time.Second
)

func Migrate(ctx context.Context, pool *pgxpool.Pool, schema string, files fs.FS) error {
	if err := ValidSchema(schema); err != nil {
		return err
	}
	logger := logging.New("postgres.migrator")
	delay := initialRetryDelay
	for {
		err := applyAll(ctx, pool, schema, files)
		if err == nil {
			logger.Info(fmt.Sprintf("Database schema %s is migrated", schema))
			return nil
		}
		if !IsUnavailable(err) {
			return err
		}
		logger.Warn(fmt.Sprintf("Database unavailable (%s), retrying migrations in %s", err.Error(), delay))
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-time.After(delay):
		}
		delay = min(delay*2, maxRetryDelay)
	}
}

func applyAll(ctx context.Context, pool *pgxpool.Pool, schema string, files fs.FS) error {
	names, err := fs.Glob(files, "*.sql")
	if err != nil {
		return err
	}
	sort.Strings(names)
	for _, name := range names {
		content, err := fs.ReadFile(files, name)
		if err != nil {
			return err
		}
		if err := applyOne(ctx, pool, schema, name, string(content)); err != nil {
			return err
		}
	}
	return nil
}

func applyOne(ctx context.Context, pool *pgxpool.Pool, schema, name, content string) error {
	transaction, err := pool.Begin(ctx)
	if err != nil {
		return err
	}
	defer transaction.Rollback(context.WithoutCancel(ctx))
	quoted := pgx.Identifier{schema}.Sanitize()
	if _, err := transaction.Exec(ctx, "select pg_advisory_xact_lock(hashtext('relay_migrations_' || $1::text))", schema); err != nil {
		return err
	}
	if _, err := transaction.Exec(ctx, "create schema if not exists "+quoted); err != nil {
		return err
	}
	if _, err := transaction.Exec(ctx, "set local search_path to "+quoted); err != nil {
		return err
	}
	if _, err := transaction.Exec(ctx, "create table if not exists schema_migrations (name text primary key, applied_at timestamptz not null)"); err != nil {
		return err
	}
	var applied bool
	if err := transaction.QueryRow(ctx, "select exists (select 1 from schema_migrations where name = $1)", name).Scan(&applied); err != nil {
		return err
	}
	if applied {
		return transaction.Commit(ctx)
	}
	if _, err := transaction.Exec(ctx, content); err != nil {
		return fmt.Errorf("migration %s failed: %w", name, err)
	}
	if _, err := transaction.Exec(ctx, "insert into schema_migrations (name, applied_at) values ($1, now())", name); err != nil {
		return err
	}
	return transaction.Commit(ctx)
}
