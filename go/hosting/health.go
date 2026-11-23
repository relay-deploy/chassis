package hosting

import (
	"context"
	"sync"
)

type Check func(ctx context.Context) error

type namedCheck struct {
	name  string
	check Check
}

type Health struct {
	lock   sync.RWMutex
	checks []namedCheck
}

func NewHealth() *Health {
	return &Health{}
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
	for _, item := range h.snapshot() {
		if err := item.check(ctx); err != nil {
			return false
		}
	}
	return true
}
