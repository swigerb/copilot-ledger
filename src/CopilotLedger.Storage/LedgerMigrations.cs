namespace CopilotLedger.Storage;

internal static class LedgerMigrations
{
    public static IReadOnlyList<(int Version, string Sql)> Migrations { get; } =
    [
        (1, """
            CREATE TABLE spans (
                id                       INTEGER PRIMARY KEY,
                trace_id                 TEXT    NOT NULL,
                span_id                  TEXT    NOT NULL,
                parent_span_id           TEXT,
                operation_name           TEXT    NOT NULL,
                conversation_id          TEXT,
                agent_name               TEXT,
                provider_name            TEXT,
                request_model            TEXT,
                response_model           TEXT,
                model_id                 TEXT,
                rate_card_model_id       TEXT,
                tier_name                TEXT,
                server_address           TEXT,
                server_port              INTEGER,
                start_time_unix_ms       INTEGER NOT NULL,
                end_time_unix_ms         INTEGER NOT NULL,
                input_tokens             INTEGER NOT NULL CHECK (input_tokens >= 0),
                output_tokens            INTEGER NOT NULL CHECK (output_tokens >= 0),
                cache_read_input_tokens  INTEGER NOT NULL CHECK (cache_read_input_tokens >= 0),
                cache_write_input_tokens INTEGER NOT NULL CHECK (cache_write_input_tokens >= 0),
                billing_channel          TEXT    NOT NULL,
                pricing_status           TEXT    NOT NULL,
                rates_version            TEXT    NOT NULL,
                cost_usd_pico            INTEGER NOT NULL CHECK (cost_usd_pico >= 0),
                saved_usd_pico           INTEGER NOT NULL CHECK (saved_usd_pico >= 0),
                repo_root                TEXT,
                repo_slug                TEXT,
                branch_name              TEXT,
                head_commit_sha          TEXT,
                recorded_at_unix_ms      INTEGER NOT NULL,
                UNIQUE (trace_id, span_id)
            ) STRICT;

            CREATE INDEX ix_spans_start_time  ON spans (start_time_unix_ms);
            CREATE INDEX ix_spans_repo_branch ON spans (repo_slug, branch_name);
            CREATE INDEX ix_spans_model       ON spans (model_id);
            CREATE INDEX ix_spans_agent       ON spans (agent_name);
            """),
        (2, """
            CREATE TABLE span_links (
                trace_id            TEXT    NOT NULL,
                span_id             TEXT    NOT NULL,
                parent_span_id      TEXT,
                operation_name      TEXT,
                agent_name          TEXT,
                conversation_id     TEXT,
                git_repository_slug TEXT,
                git_branch          TEXT,
                git_commit_sha      TEXT,
                start_time_unix_ms  INTEGER NOT NULL,
                recorded_at_unix_ms INTEGER NOT NULL,
                PRIMARY KEY (trace_id, span_id)
            ) STRICT, WITHOUT ROWID;

            CREATE INDEX ix_span_links_parent ON span_links (trace_id, parent_span_id);
            CREATE INDEX ix_span_links_start  ON span_links (start_time_unix_ms);
            CREATE INDEX ix_spans_trace       ON spans (trace_id);
            """),
        (3, """
            ALTER TABLE span_links ADD COLUMN tool_name TEXT;
            ALTER TABLE span_links ADD COLUMN tool_exit_code INTEGER;
            ALTER TABLE span_links ADD COLUMN status_code INTEGER NOT NULL DEFAULT 0;

            CREATE TABLE breaker_trips (
                id                   INTEGER PRIMARY KEY,
                rule                 TEXT    NOT NULL CHECK (rule IN ('credit_velocity', 'retry_churn')),
                trace_id             TEXT    NOT NULL,
                conversation_id      TEXT,
                repository           TEXT    NOT NULL,
                branch               TEXT    NOT NULL,
                repo_root            TEXT,
                tripped_at_unix_ms   INTEGER NOT NULL,
                detail               TEXT    NOT NULL,
                window_cost_usd_pico INTEGER CHECK (window_cost_usd_pico >= 0),
                window_minutes       INTEGER,
                failure_count        INTEGER,
                recorded_at_unix_ms  INTEGER NOT NULL,
                notified_at_unix_ms  INTEGER,
                reset_at_unix_ms     INTEGER,
                UNIQUE (rule, trace_id)
            ) STRICT;

            CREATE INDEX ix_breaker_trips_open ON breaker_trips (reset_at_unix_ms, repository, branch);

            CREATE TABLE branch_budgets (
                repository          TEXT    NOT NULL,
                branch              TEXT    NOT NULL,
                budget_usd_pico     INTEGER NOT NULL CHECK (budget_usd_pico > 0),
                set_at_unix_ms      INTEGER NOT NULL,
                alerted_at_unix_ms  INTEGER,
                notified_at_unix_ms INTEGER,
                PRIMARY KEY (repository, branch)
            ) STRICT, WITHOUT ROWID;

            CREATE TABLE breaker_settings (
                id                          INTEGER PRIMARY KEY CHECK (id = 1),
                velocity_threshold_usd_pico INTEGER NOT NULL CHECK (velocity_threshold_usd_pico > 0),
                velocity_window_minutes     INTEGER NOT NULL CHECK (velocity_window_minutes BETWEEN 1 AND 1440),
                churn_failures              INTEGER NOT NULL CHECK (churn_failures BETWEEN 2 AND 100),
                flag_file                   INTEGER NOT NULL CHECK (flag_file IN (0, 1)),
                notifications               INTEGER NOT NULL CHECK (notifications IN (0, 1)),
                updated_at_unix_ms          INTEGER NOT NULL
            ) STRICT;

            INSERT INTO breaker_settings (id, velocity_threshold_usd_pico, velocity_window_minutes, churn_failures, flag_file, notifications, updated_at_unix_ms)
            VALUES (1, 1500000000000, 5, 5, 0, 1, 0);
            """),
    ];
}
