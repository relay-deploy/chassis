package postgres

import (
	"context"

	"github.com/jackc/pgx/v5/pgxpool"
)

func Open(ctx context.Context, connectionString, schema string) (*pgxpool.Pool, error) {
	if err := ValidSchema(schema); err != nil {
		return nil, err
	}
	config, err := parseConnectionString(connectionString, schema)
	if err != nil {
		return nil, err
	}
	return pgxpool.NewWithConfig(ctx, config)
}
