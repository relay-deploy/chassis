package fault

import "errors"

type Kind int

const (
	Validation Kind = iota + 1
	NotFound
	Conflict
	Unavailable
)

type Error struct {
	Kind    Kind
	Code    string
	Message string
}

func (e *Error) Error() string {
	return e.Message
}

func New(kind Kind, code, message string) *Error {
	return &Error{Kind: kind, Code: code, Message: message}
}

func KindOf(err error) (Kind, bool) {
	var fault *Error
	if errors.As(err, &fault) {
		return fault.Kind, true
	}
	return 0, false
}

func IsUnavailable(err error) bool {
	kind, ok := KindOf(err)
	return ok && kind == Unavailable
}

func IsRejection(err error) bool {
	kind, ok := KindOf(err)
	return ok && (kind == Validation || kind == NotFound || kind == Conflict)
}
