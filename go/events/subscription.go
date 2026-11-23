package events

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"sync"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/relay-deploy/chassis/go/fault"
	"github.com/relay-deploy/chassis/go/hosting"
	"github.com/relay-deploy/chassis/go/logging"
	"github.com/relay-deploy/chassis/go/postgres"
)

type subscription struct {
	pool          *pgxpool.Pool
	consumer      string
	source        string
	handle        Handler
	options       Options
	logger        *slog.Logger
	readCommitted string
	cursor        string
	advanceCursor string
}

func Subscription(pool *pgxpool.Pool, consumer, source string, handle Handler, options Options) hosting.Component {
	reader := &subscription{
		pool:          pool,
		consumer:      consumer,
		source:        source,
		handle:        handle,
		options:       options,
		logger:        logging.New("events.subscription").With("source", source),
		readCommitted: readCommittedSQL(source),
		cursor:        cursorSQL(consumer),
		advanceCursor: advanceSQL(consumer),
	}
	return hosting.Component{Name: "events:" + source, Run: reader.run}
}

func (s *subscription) run(parent context.Context) error {
	if err := errors.Join(postgres.ValidSchema(s.consumer), postgres.ValidSchema(s.source), s.options.validate()); err != nil {
		return err
	}
	ctx, cancel := context.WithCancel(parent)
	wake := make(chan struct{}, 1)
	var listener sync.WaitGroup
	listener.Go(func() { s.listen(ctx, wake) })
	err := s.consume(ctx, wake)
	cancel()
	listener.Wait()
	return err
}

func (s *subscription) listen(ctx context.Context, wake chan<- struct{}) {
	for {
		err := postgres.Listen(ctx, s.pool, eventChannel(s.source), wake)
		if ctx.Err() != nil {
			return
		}
		message := fmt.Sprintf("Listening for events from %s failed (%s), retrying in %s", s.source, err, s.options.RetryDelay)
		if postgres.IsUnavailable(err) {
			s.logger.Warn(message)
		} else {
			s.logger.Error(message)
		}
		wait(ctx, nil, s.options.RetryDelay)
	}
}

func (s *subscription) consume(ctx context.Context, wake <-chan struct{}) error {
	delay := newBackoff(s.options.RetryDelay, s.options.MaxRetryDelay)
	for {
		err := s.drain(ctx, delay)
		if ctx.Err() != nil {
			return nil
		}
		var stalled *stalledEvent
		switch {
		case err == nil:
			delay.reset()
			wait(ctx, wake, s.options.FallbackPoll)
		case errors.As(err, &stalled):
			pause := delay.next()
			s.reportStalled(stalled, pause)
			wait(ctx, nil, pause)
		case postgres.IsUnavailable(err) || isSourceMissing(err):
			pause := delay.next()
			s.logger.Warn(fmt.Sprintf("Events from %s are unavailable (%s), retrying in %s", s.source, err, pause))
			wait(ctx, wake, pause)
		default:
			return err
		}
	}
}

func (s *subscription) drain(ctx context.Context, delay *backoff) error {
	from, err := s.position(ctx)
	if err != nil {
		return err
	}
	for {
		batch, err := s.read(ctx, from)
		if err != nil || len(batch) == 0 {
			return err
		}
		for _, event := range batch {
			if err := s.process(ctx, event); err != nil {
				return &stalledEvent{event: event, cause: err}
			}
			delay.reset()
			from = event.position()
		}
	}
}

func (s *subscription) position(ctx context.Context) (position, error) {
	var at position
	err := s.pool.QueryRow(ctx, s.cursor, s.source).Scan(&at.transactionID, &at.sequence)
	if errors.Is(err, pgx.ErrNoRows) {
		return position{}, nil
	}
	return at, err
}

func (s *subscription) read(ctx context.Context, from position) ([]Event, error) {
	rows, err := s.pool.Query(ctx, s.readCommitted, from.transactionID, from.sequence, s.options.BatchSize)
	if err != nil {
		return nil, err
	}
	return pgx.CollectRows(rows, scanEvent)
}

func scanEvent(row pgx.CollectableRow) (Event, error) {
	var event Event
	var id, subject pgtype.UUID
	err := row.Scan(&event.Sequence, &id, &event.Type, &subject, &event.Payload, &event.OccurredAt, &event.TransactionID)
	event.ID = id.String()
	event.SubjectID = subject.String()
	return event, err
}

func (s *subscription) process(ctx context.Context, event Event) error {
	err := pgx.BeginFunc(ctx, s.pool, func(tx pgx.Tx) error {
		if err := s.handle(ctx, tx, event); err != nil {
			return err
		}
		return s.advance(ctx, tx, event)
	})
	if !fault.IsRejection(err) {
		return err
	}
	s.logger.Error(fmt.Sprintf("Handler for %s rejected event %s (%s), skipping it", event.Type, event.ID, err))
	return pgx.BeginFunc(ctx, s.pool, func(tx pgx.Tx) error {
		return s.advance(ctx, tx, event)
	})
}

func (s *subscription) advance(ctx context.Context, tx pgx.Tx, event Event) error {
	_, err := tx.Exec(ctx, s.advanceCursor, s.source, event.TransactionID, event.Sequence)
	return err
}

func (s *subscription) reportStalled(stalled *stalledEvent, pause time.Duration) {
	message := fmt.Sprintf("Handler for %s failed on event %s (%s), retrying in %s", stalled.event.Type, stalled.event.ID, stalled.cause, pause)
	if fault.IsUnavailable(stalled.cause) || postgres.IsUnavailable(stalled.cause) {
		s.logger.Warn(message)
		return
	}
	s.logger.Error(message)
}

type stalledEvent struct {
	event Event
	cause error
}

func (s *stalledEvent) Error() string {
	return s.cause.Error()
}

func (s *stalledEvent) Unwrap() error {
	return s.cause
}

type backoff struct {
	initial time.Duration
	limit   time.Duration
	current time.Duration
}

func newBackoff(initial, limit time.Duration) *backoff {
	return &backoff{initial: initial, limit: limit, current: initial}
}

func (b *backoff) next() time.Duration {
	pause := b.current
	b.current = min(b.current*2, b.limit)
	return pause
}

func (b *backoff) reset() {
	b.current = b.initial
}

func wait(ctx context.Context, wake <-chan struct{}, pause time.Duration) {
	timer := time.NewTimer(pause)
	defer timer.Stop()
	select {
	case <-ctx.Done():
	case <-wake:
	case <-timer.C:
	}
}
