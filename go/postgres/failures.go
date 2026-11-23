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
	var pgError *pgconn.PgError
	if errors.As(err, &pgError) {
		return isUnavailableCode(pgError.Code)
	}
	var connectError *pgconn.ConnectError
	if errors.As(err, &connectError) {
		return true
	}
	var networkError net.Error
	if errors.As(err, &networkError) {
		return true
	}
	return errors.Is(err, io.EOF) || errors.Is(err, io.ErrUnexpectedEOF)
}

func isUnavailableCode(code string) bool {
	return strings.HasPrefix(code, "08") ||
		code == "57P01" ||
		code == "57P02" ||
		code == "57P03" ||
		code == "53300"
}
