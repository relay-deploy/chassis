package postgres

import (
	"fmt"
	"strconv"
	"strings"

	"github.com/jackc/pgx/v5/pgxpool"
)

func parseConnectionString(connectionString, schema string) (*pgxpool.Config, error) {
	config, err := pgxpool.ParseConfig("")
	if err != nil {
		return nil, err
	}
	sslMode := ""
	for _, pair := range strings.Split(connectionString, ";") {
		if strings.TrimSpace(pair) == "" {
			continue
		}
		key, value, found := strings.Cut(pair, "=")
		if !found {
			return nil, fmt.Errorf("connection string part '%s' is not Key=Value", strings.TrimSpace(pair))
		}
		key = strings.ToLower(strings.TrimSpace(key))
		value = strings.TrimSpace(value)
		switch key {
		case "host":
			config.ConnConfig.Host = value
		case "port":
			port, err := strconv.ParseUint(value, 10, 16)
			if err != nil {
				return nil, fmt.Errorf("connection string port '%s' is invalid", value)
			}
			config.ConnConfig.Port = uint16(port)
		case "username", "user id":
			config.ConnConfig.User = value
		case "password":
			config.ConnConfig.Password = value
		case "database":
			config.ConnConfig.Database = value
		case "ssl mode":
			sslMode = strings.ToLower(strings.ReplaceAll(value, " ", "-"))
		default:
			return nil, fmt.Errorf("connection string key '%s' is not supported", key)
		}
	}
	if sslMode != "" {
		withMode, err := pgxpool.ParseConfig(fmt.Sprintf("sslmode=%s host=%s", sslMode, config.ConnConfig.Host))
		if err != nil {
			return nil, fmt.Errorf("connection string SSL Mode '%s' is invalid: %w", sslMode, err)
		}
		config.ConnConfig.TLSConfig = withMode.ConnConfig.TLSConfig
		config.ConnConfig.Fallbacks = withMode.ConnConfig.Fallbacks
	}
	config.ConnConfig.RuntimeParams["search_path"] = schema
	return config, nil
}
