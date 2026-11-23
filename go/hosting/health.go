package hosting

import (
	"context"
	"log/slog"
	"sync"

	"github.com/relay-deploy/chassis/go/logging"
)

type Check func(ctx context.Context) error

type namedCheck struct {
	name  string
	check Check
}

type Health struct {
	lock   sync.RWMutex
	checks []namedCheck
	logger *slog.Logger
}

func NewHealth() *Health {
	return &Health{logger: logging.New("hosting.health")}
}

func (h *Health) AddReadiness(name string, check Check) {
	h.lock.Lock()
	defer h.lock.Unlock()
	h.checks = append(h.checks, namedCheck{name: name, check: check})
}

func (h *Health) snapshot() []namedCheck {
	h.lock.RLock()
	defer h.lock.RUnlock()
	return append([]namedCheck(nil), h.checks...)
}

func (h *Health) ready(ctx context.Context) bool {
	checks := h.snapshot()
	type outcome struct {
		name   string
		passed bool
	}
	results := make(chan outcome, len(checks))
	pending := make(map[string]bool, len(checks))
	for _, item := range checks {
		pending[item.name] = true
		go func() {
			err := item.check(ctx)
			if err != nil {
				h.logger.Warn("Readiness check failed", "check", item.name, "reason", err.Error())
			}
			results <- outcome{name: item.name, passed: err == nil}
		}()
	}
	allPassed := true
	for range checks {
		select {
		case result := <-results:
			delete(pending, result.name)
			allPassed = allPassed && result.passed
		case <-ctx.Done():
			for name := range pending {
				h.logger.Warn("Readiness check timed out", "check", name, "reason", ctx.Err().Error())
			}
			return false
		}
	}
	return allPassed
}
