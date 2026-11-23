package logging

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"log/slog"
	"os"
	"sync"
	"time"
)

const timestampLayout = "2006-01-02T15:04:05.000Z"

type entry struct {
	key   string
	value any
}

type node struct {
	entries []entry
}

func (n *node) clone() *node {
	copied := &node{entries: make([]entry, len(n.entries))}
	for index, item := range n.entries {
		if child, ok := item.value.(*node); ok {
			copied.entries[index] = entry{key: item.key, value: child.clone()}
		} else {
			copied.entries[index] = item
		}
	}
	return copied
}

func (n *node) child(name string) *node {
	for _, item := range n.entries {
		if child, ok := item.value.(*node); ok && item.key == name {
			return child
		}
	}
	created := &node{}
	n.entries = append(n.entries, entry{key: name, value: created})
	return created
}

func (n *node) add(attr slog.Attr) {
	value := attr.Value.Resolve()
	if value.Kind() == slog.KindGroup {
		members := value.Group()
		if len(members) == 0 {
			return
		}
		target := n
		if attr.Key != "" {
			target = &node{}
		}
		for _, member := range members {
			target.add(member)
		}
		if attr.Key != "" {
			n.entries = append(n.entries, entry{key: attr.Key, value: target})
		}
		return
	}
	if attr.Equal(slog.Attr{}) {
		return
	}
	n.entries = append(n.entries, entry{key: attr.Key, value: plain(value)})
}

func (n *node) empty() bool {
	for _, item := range n.entries {
		if child, ok := item.value.(*node); ok {
			if !child.empty() {
				return false
			}
			continue
		}
		return false
	}
	return true
}

func (n *node) write(buffer *bytes.Buffer) {
	buffer.WriteByte('{')
	first := true
	for _, item := range n.entries {
		child, isNode := item.value.(*node)
		if isNode && child.empty() {
			continue
		}
		if !first {
			buffer.WriteByte(',')
		}
		first = false
		writeJSON(buffer, item.key)
		buffer.WriteByte(':')
		if isNode {
			child.write(buffer)
		} else {
			writeJSON(buffer, item.value)
		}
	}
	buffer.WriteByte('}')
}

func plain(value slog.Value) any {
	switch value.Kind() {
	case slog.KindString:
		return value.String()
	case slog.KindInt64:
		return value.Int64()
	case slog.KindUint64:
		return value.Uint64()
	case slog.KindFloat64:
		return value.Float64()
	case slog.KindBool:
		return value.Bool()
	case slog.KindDuration:
		return value.Duration().String()
	case slog.KindTime:
		return value.Time().UTC().Format(timestampLayout)
	default:
		raw := value.Any()
		if err, ok := raw.(error); ok {
			return err.Error()
		}
		if _, err := json.Marshal(raw); err != nil {
			return fmt.Sprint(raw)
		}
		return raw
	}
}

func writeJSON(buffer *bytes.Buffer, value any) {
	encoded, err := json.Marshal(value)
	if err != nil {
		encoded, _ = json.Marshal(fmt.Sprint(value))
	}
	buffer.Write(encoded)
}

func levelName(level slog.Level) string {
	switch {
	case level >= slog.LevelError:
		return "Error"
	case level >= slog.LevelWarn:
		return "Warning"
	case level >= slog.LevelInfo:
		return "Information"
	default:
		return "Debug"
	}
}

type handler struct {
	category string
	lock     *sync.Mutex
	state    *node
	groups   []string
}

func New(category string) *slog.Logger {
	return slog.New(&handler{category: category, lock: &sync.Mutex{}, state: &node{}})
}

func (h *handler) Enabled(_ context.Context, level slog.Level) bool {
	return level >= slog.LevelInfo
}

func (h *handler) open(root *node) *node {
	target := root
	for _, name := range h.groups {
		target = target.child(name)
	}
	return target
}

func (h *handler) WithAttrs(attrs []slog.Attr) slog.Handler {
	state := h.state.clone()
	target := h.open(state)
	for _, attr := range attrs {
		target.add(attr)
	}
	return &handler{category: h.category, lock: h.lock, state: state, groups: h.groups}
}

func (h *handler) WithGroup(name string) slog.Handler {
	if name == "" {
		return h
	}
	groups := make([]string, len(h.groups), len(h.groups)+1)
	copy(groups, h.groups)
	return &handler{category: h.category, lock: h.lock, state: h.state, groups: append(groups, name)}
}

func (h *handler) Handle(_ context.Context, record slog.Record) error {
	state := h.state.clone()
	target := h.open(state)
	record.Attrs(func(attr slog.Attr) bool {
		target.add(attr)
		return true
	})
	timestamp := record.Time
	if timestamp.IsZero() {
		timestamp = time.Now()
	}
	var buffer bytes.Buffer
	buffer.WriteString(`{"Timestamp":`)
	writeJSON(&buffer, timestamp.UTC().Format(timestampLayout))
	buffer.WriteString(`,"LogLevel":`)
	writeJSON(&buffer, levelName(record.Level))
	buffer.WriteString(`,"Category":`)
	writeJSON(&buffer, h.category)
	buffer.WriteString(`,"Message":`)
	writeJSON(&buffer, record.Message)
	if !state.empty() {
		buffer.WriteString(`,"State":`)
		state.write(&buffer)
	}
	buffer.WriteString("}\n")
	h.lock.Lock()
	defer h.lock.Unlock()
	_, err := os.Stdout.Write(buffer.Bytes())
	return err
}
