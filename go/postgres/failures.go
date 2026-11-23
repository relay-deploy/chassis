package postgres

import (
	"context"
	"errors"
	"io"
	"net"
	"strings"

	"github.com/jackc/pgx/v5/pgconn"
)

func IsUnavailable(err error) bool {
	if err == nil || errors.Is(err, context.Canceled) || errors.Is(err, context.DeadlineExceeded) {
		return false
	}
	var connectError *pgconn.ConnectError
	if errors.As(err, &connectError) {
		return true
	}
	var networkError net.Error
	if errors.As(err, &networkError) {
		return true
	}
	if errors.Is(err, io.EOF) || errors.Is(err, io.ErrUnexpectedEOF) {
		return true
	}
	var pgError *pgconn.PgError
	if errors.As(err, &pgError) {
		return strings.HasPrefix(pgError.Code, "08") ||
			pgError.Code == "57P01" ||
			pgError.Code == "57P02" ||
			pgError.Code == "57P03" ||
			pgError.Code == "53300"
	}
	return false
}
