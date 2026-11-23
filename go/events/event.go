package events

import (
	"context"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/relay-deploy/chassis/go/fault"
)

type Event struct {
	Sequence      int64
	ID            string
	Type          string
	SubjectID     string
	Payload       []byte
	OccurredAt    time.Time
	TransactionID int64
}

type Handler func(ctx context.Context, tx pgx.Tx, event Event) error

type Options struct {
	BatchSize     int
	FallbackPoll  time.Duration
	RetryDelay    time.Duration
	MaxRetryDelay time.Duration
}

func DefaultOptions() Options {
	return Options{
		BatchSize:     100,
		FallbackPoll:  10 * time.Second,
		RetryDelay:    5 * time.Second,
		MaxRetryDelay: time.Minute,
	}
}

func (o Options) validate() error {
	if o.BatchSize < 1 || o.FallbackPoll <= 0 || o.RetryDelay <= 0 || o.MaxRetryDelay < o.RetryDelay {
		return fault.New(fault.Validation, "events.invalid_options",
			"Event subscription options need a positive BatchSize, FallbackPoll and RetryDelay, and a MaxRetryDelay no shorter than RetryDelay.")
	}
	return nil
}

type position struct {
	transactionID int64
	sequence      int64
}

func (e Event) position() position {
	return position{transactionID: e.TransactionID, sequence: e.Sequence}
}
