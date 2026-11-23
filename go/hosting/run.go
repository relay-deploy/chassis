package hosting

import (
	"context"
	"log/slog"
	"os"
	"os/signal"
	"sync"
	"syscall"
)

type Component struct {
	Name string
	Run  func(ctx context.Context) error
}

func Run(logger *slog.Logger, components ...Component) int {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	ctx, cancel := context.WithCancel(ctx)
	defer cancel()

	var group sync.WaitGroup
	var lock sync.Mutex
	failed := false
	for _, component := range components {
		group.Add(1)
		go func() {
			defer group.Done()
			if err := component.Run(ctx); err != nil {
				logger.Error("Component failed", "component", component.Name, "error", err)
				lock.Lock()
				failed = true
				lock.Unlock()
				cancel()
			}
		}()
	}
	group.Wait()
	if failed {
		return 1
	}
	return 0
}
