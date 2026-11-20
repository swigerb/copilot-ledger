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
    ];
}
