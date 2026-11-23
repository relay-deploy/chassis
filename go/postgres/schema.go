package postgres

import (
	"regexp"

	"github.com/relay-deploy/chassis/go/fault"
)

var (
	schemaPattern  = regexp.MustCompile(`^[a-z][a-z0-9_]{0,40}$`)
	channelPattern = regexp.MustCompile(`^[a-z][a-z0-9_]{0,62}$`)
)

func ValidSchema(name string) error {
	return validIdentifier(schemaPattern, name, "schema")
}

func validChannel(name string) error {
	return validIdentifier(channelPattern, name, "channel")
}

func validIdentifier(pattern *regexp.Regexp, name, kind string) error {
	if !pattern.MatchString(name) {
		return fault.New(fault.Validation, "postgres.invalid_identifier", "'"+name+"' is not a valid "+kind+" name.")
	}
	return nil
}
