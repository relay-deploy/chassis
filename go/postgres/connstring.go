package postgres

import (
	"errors"
	"fmt"
	"strconv"
	"strings"

	"github.com/jackc/pgx/v5/pgxpool"
)

type keyword struct {
	libpq   string
	display string
}

var (
	host     = keyword{libpq: "host", display: "Host"}
	port     = keyword{libpq: "port", display: "Port"}
	user     = keyword{libpq: "user", display: "Username"}
	password = keyword{libpq: "password", display: "Password"}
	database = keyword{libpq: "dbname", display: "Database"}
	sslMode  = keyword{libpq: "sslmode", display: "SSL Mode"}
)

var keywords = map[string]keyword{
	"host":      host,
	"server":    host,
	"port":      port,
	"username":  user,
	"user name": user,
	"user id":   user,
	"userid":    user,
	"uid":       user,
	"password":  password,
	"pwd":       password,
	"psw":       password,
	"database":  database,
	"db":        database,
	"ssl mode":  sslMode,
	"sslmode":   sslMode,
}

var sslModes = map[string]string{
	"disable":    "disable",
	"allow":      "allow",
	"prefer":     "prefer",
	"require":    "require",
	"verifyca":   "verify-ca",
	"verifyfull": "verify-full",
}

var libpqEscaper = strings.NewReplacer(`\`, `\\`, `'`, `\'`)

func parseConnectionString(connectionString, schema string) (*pgxpool.Config, error) {
	pairs, err := split(connectionString)
	if err != nil {
		return nil, err
	}
	settings := make([]string, 0, len(pairs))
	for _, pair := range pairs {
		translated, err := translate(pair.keyword, pair.value)
		if err != nil {
			return nil, err
		}
		settings = append(settings, pair.keyword.libpq+"='"+libpqEscaper.Replace(translated)+"'")
	}
	config, err := pgxpool.ParseConfig(strings.Join(settings, " "))
	if err != nil {
		return nil, errors.New("connection string cannot be turned into a pgx configuration")
	}
	config.ConnConfig.RuntimeParams["search_path"] = schema
	return config, nil
}

type pair struct {
	keyword keyword
	value   string
}

func split(connectionString string) ([]pair, error) {
	var pairs []pair
	rest := connectionString
	for part := 1; ; part++ {
		rest = strings.TrimLeft(rest, " \t\r\n;")
		if rest == "" {
			return pairs, nil
		}
		end := strings.IndexAny(rest, "=;")
		if end < 0 || rest[end] == ';' {
			return nil, fmt.Errorf("connection string part %d is not Key=Value", part)
		}
		known, supported := keywords[strings.ToLower(strings.TrimSpace(rest[:end]))]
		if !supported {
			return nil, fmt.Errorf("connection string part %d has a key that isn't supported", part)
		}
		value, remaining, err := valueOf(rest[end+1:], known)
		if err != nil {
			return nil, err
		}
		pairs = append(pairs, pair{keyword: known, value: value})
		rest = remaining
	}
}

func valueOf(text string, known keyword) (string, string, error) {
	trimmed := strings.TrimLeft(text, " \t")
	if trimmed == "" || (trimmed[0] != '"' && trimmed[0] != '\'') {
		value, remaining, _ := strings.Cut(text, ";")
		return strings.TrimSpace(value), remaining, nil
	}
	quote := trimmed[0]
	var value strings.Builder
	for index := 1; index < len(trimmed); index++ {
		if trimmed[index] != quote {
			value.WriteByte(trimmed[index])
			continue
		}
		if index+1 < len(trimmed) && trimmed[index+1] == quote {
			value.WriteByte(quote)
			index++
			continue
		}
		remaining := strings.TrimLeft(trimmed[index+1:], " \t")
		if remaining != "" && remaining[0] != ';' {
			return "", "", fmt.Errorf("connection string %s has text after its closing quote", known.display)
		}
		return value.String(), remaining, nil
	}
	return "", "", fmt.Errorf("connection string %s has a quote that is never closed", known.display)
}

func translate(known keyword, value string) (string, error) {
	switch known {
	case port:
		if number, err := strconv.ParseUint(value, 10, 16); err != nil || number == 0 {
			return "", fmt.Errorf("connection string %s is not a number from 1 to 65535", known.display)
		}
	case sslMode:
		mode, supported := sslModes[strings.ToLower(strings.ReplaceAll(value, " ", ""))]
		if !supported {
			return "", fmt.Errorf("connection string %s is not Disable, Allow, Prefer, Require, VerifyCA or VerifyFull", known.display)
		}
		return mode, nil
	}
	return value, nil
}
