package postgres

import (
	"context"

	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/relay-deploy/chassis/go/hosting"
)

func Readiness(pool *pgxpool.Pool) hosting.Check {
	return func(ctx context.Context) error {
		return pool.Ping(ctx)
	}
}
