package hosting

import (
	"context"
	"errors"
	"fmt"
	"net"
	"net/http"
	"time"
)

const (
	readinessTimeout = 5 * time.Second
	shutdownTimeout  = 10 * time.Second
	healthy          = "Healthy"
	unhealthy        = "Unhealthy"
)

func ManagementServer(port int, health *Health) Component {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health/live", func(w http.ResponseWriter, _ *http.Request) {
		respond(w, http.StatusOK, healthy)
	})
	mux.HandleFunc("GET /health/ready", func(w http.ResponseWriter, r *http.Request) {
		ctx, cancel := context.WithTimeout(r.Context(), readinessTimeout)
		defer cancel()
		if health.ready(ctx) {
			respond(w, http.StatusOK, healthy)
			return
		}
		respond(w, http.StatusServiceUnavailable, unhealthy)
	})
	return Component{
		Name: "management",
		Run: func(ctx context.Context) error {
			listener, err := net.Listen("tcp4", fmt.Sprintf("0.0.0.0:%d", port))
			if err != nil {
				return err
			}
			server := &http.Server{Handler: mux, ReadHeaderTimeout: readinessTimeout}
			failed := make(chan error, 1)
			go func() {
				failed <- server.Serve(listener)
			}()
			select {
			case err := <-failed:
				return err
			case <-ctx.Done():
				stopping, cancel := context.WithTimeout(context.Background(), shutdownTimeout)
				defer cancel()
				if err := server.Shutdown(stopping); err != nil && !errors.Is(err, http.ErrServerClosed) {
					return err
				}
				return nil
			}
		},
	}
}

func respond(w http.ResponseWriter, status int, body string) {
	w.Header().Set("Content-Type", "text/plain")
	w.WriteHeader(status)
	_, _ = w.Write([]byte(body))
}
