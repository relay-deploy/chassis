CREATE TABLE job (
    id uuid PRIMARY KEY,
    kind text NOT NULL,
    payload jsonb NOT NULL,
    idempotency_key text NOT NULL UNIQUE,
    run_at timestamptz NOT NULL,
    attempts integer NOT NULL,
    max_attempts integer NOT NULL,
    locked_until timestamptz,
    last_error text,
    failed_at timestamptz,
    created_at timestamptz NOT NULL
);

CREATE INDEX job_ready ON job (run_at) WHERE failed_at IS NULL;
