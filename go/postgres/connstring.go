package postgres

import (
	"fmt"
	"strconv"
	"strings"

	"github.com/jackc/pgx/v5/pgxpool"
)

var keywords = map[string]string{
	"host":     "host",
	"port":     "port",
	"username": "user",
	"user id":  "user",
	"password": "password",
	"database": "dbname",
	"ssl mode": "sslmode",
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
	var settings []string
	for _, pair := range strings.Split(connectionString, ";") {
		if strings.TrimSpace(pair) == "" {
			continue
		}
		key, value, found := strings.Cut(pair, "=")
		if !found {
			return nil, fmt.Errorf("connection string part '%s' is not Key=Value", strings.TrimSpace(pair))
		}
		key = strings.ToLower(strings.TrimSpace(key))
		keyword, supported := keywords[key]
		if !supported {
			return nil, fmt.Errorf("connection string key '%s' is not supported", key)
		}
		translated, err := translate(keyword, strings.TrimSpace(value))
		if err != nil {
			return nil, err
		}
		settings = append(settings, keyword+"='"+libpqEscaper.Replace(translated)+"'")
	}
	config, err := pgxpool.ParseConfig(strings.Join(settings, " "))
	if err != nil {
		return nil, err
	}
	config.ConnConfig.RuntimeParams["search_path"] = schema
	return config, nil
}

func translate(keyword, value string) (string, error) {
	switch keyword {
	case "port":
		if _, err := strconv.ParseUint(value, 10, 16); err != nil {
			return "", fmt.Errorf("connection string port '%s' is invalid", value)
		}
	case "sslmode":
		mode, known := sslModes[strings.ToLower(strings.ReplaceAll(value, " ", ""))]
		if !known {
			return "", fmt.Errorf("connection string SSL Mode '%s' is not supported", value)
		}
		return mode, nil
	}
	return value, nil
}
