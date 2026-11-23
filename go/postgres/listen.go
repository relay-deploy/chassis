package postgres

import (
	"context"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
)

func Listen(ctx context.Context, pool *pgxpool.Pool, channel string, wake chan<- struct{}) error {
	if err := ValidSchema(channel); err != nil {
		return err
	}
	connection, err := pool.Acquire(ctx)
	if err != nil {
		return stopOrFail(ctx, err)
	}
	conn := connection.Hijack()
	defer conn.Close(context.WithoutCancel(ctx))
	if _, err := conn.Exec(ctx, "listen "+pgx.Identifier{channel}.Sanitize()); err != nil {
		return stopOrFail(ctx, err)
	}
	ring(wake)
	for {
		if _, err := conn.WaitForNotification(ctx); err != nil {
			return stopOrFail(ctx, err)
		}
		ring(wake)
	}
}

func ring(wake chan<- struct{}) {
	select {
	case wake <- struct{}{}:
	default:
	}
}

func stopOrFail(ctx context.Context, err error) error {
	if ctx.Err() != nil {
		return nil
	}
	return err
}
