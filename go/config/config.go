package config

import (
	"errors"
	"fmt"
	"os"
	"strconv"
	"strings"
	"time"
)

type Reader struct {
	problems []string
}

func FromEnvironment() *Reader {
	return &Reader{}
}

func (r *Reader) Required(key string) string {
	value := os.Getenv(key)
	if value == "" {
		r.problems = append(r.problems, fmt.Sprintf("%s is required", key))
	}
	return value
}

func (r *Reader) String(key, fallback string) string {
	value := os.Getenv(key)
	if value == "" {
		return fallback
	}
	return value
}

func (r *Reader) Int(key string, fallback int) int {
	value := os.Getenv(key)
	if value == "" {
		return fallback
	}
	parsed, err := strconv.Atoi(value)
	if err != nil {
		r.problems = append(r.problems, fmt.Sprintf("%s: %s", key, err))
		return fallback
	}
	return parsed
}

func (r *Reader) Duration(key string, fallback time.Duration) time.Duration {
	value := os.Getenv(key)
	if value == "" {
		return fallback
	}
	parsed, err := time.ParseDuration(value)
	if err != nil {
		r.problems = append(r.problems, fmt.Sprintf("%s: %s", key, err))
		return fallback
	}
	return parsed
}

func (r *Reader) PositiveDuration(key string, fallback time.Duration) time.Duration {
	value := r.Duration(key, fallback)
	if value <= 0 {
		r.problems = append(r.problems, fmt.Sprintf("%s must be above zero", key))
		return fallback
	}
	return value
}

func (r *Reader) Port(key string, fallback int) int {
	value := r.Int(key, fallback)
	if value < 1 || value > 65535 {
		r.problems = append(r.problems, fmt.Sprintf("%s must be a port from 1 to 65535", key))
		return fallback
	}
	return value
}

func (r *Reader) Check(key string, err error) {
	if err != nil {
		r.problems = append(r.problems, fmt.Sprintf("%s: %s", key, err))
	}
}

func (r *Reader) List(key string, fallback []string) []string {
	value := os.Getenv(key)
	if value == "" {
		return fallback
	}
	parts := strings.Split(value, ",")
	items := make([]string, 0, len(parts))
	for _, part := range parts {
		trimmed := strings.TrimSpace(part)
		if trimmed != "" {
			items = append(items, trimmed)
		}
	}
	return items
}

func (r *Reader) Location(key, fallback string) *time.Location {
	name := r.String(key, fallback)
	location, err := time.LoadLocation(name)
	if err != nil {
		r.problems = append(r.problems, fmt.Sprintf("%s: %s", key, err))
		return time.UTC
	}
	return location
}

func (r *Reader) Err() error {
	if len(r.problems) == 0 {
		return nil
	}
	return errors.New(strings.Join(r.problems, "; "))
}
