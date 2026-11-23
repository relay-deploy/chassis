package postgres

import (
	"regexp"

	"github.com/relay-deploy/chassis/go/fault"
)

var schemaPattern = regexp.MustCompile(`^[a-z][a-z0-9_]{0,40}$`)

func ValidSchema(name string) error {
	if !schemaPattern.MatchString(name) {
		return fault.New(fault.Validation, "postgres.invalid_identifier", "'"+name+"' is not a valid schema name.")
	}
	return nil
}
