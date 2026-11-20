using CopilotLedger.Core.Pricing;
using CopilotLedger.Core.Telemetry;
using Microsoft.Data.Sqlite;

namespace CopilotLedger.Storage;

public sealed class SqliteLedgerStore
{
    private const string InsertSql = """
        INSERT INTO spans (
            trace_id, span_id, parent_span_id, operation_name, conversation_id,
            agent_name, provider_name, request_model, response_model, model_id,
            rate_card_model_id, tier_name, server_address, server_port,
            start_time_unix_ms, end_time_unix_ms, input_tokens, output_tokens,
            cache_read_input_tokens, cache_write_input_tokens, billing_channel,
            pricing_status, rates_version, cost_usd_pico, saved_usd_pico,
            repo_root, repo_slug, branch_name, head_commit_sha, recorded_at_unix_ms
        ) VALUES (
            @trace_id, @span_id, @parent_span_id, @operation_name, @conversation_id,
            @agent_name, @provider_name, @request_model, @response_model, @model_id,
            @rate_card_model_id, @tier_name, @server_address, @server_port,
            @start_time_unix_ms, @end_time_unix_ms, @input_tokens, @output_tokens,
            @cache_read_input_tokens, @cache_write_input_tokens, @billing_channel,
            @pricing_status, @rates_version, @cost_usd_pico, @saved_usd_pico,
            @repo_root, @repo_slug, @branch_name, @head_commit_sha, @recorded_at_unix_ms
        ) ON CONFLICT (trace_id, span_id) DO NOTHING;
        """;

    private readonly TimeProvider _timeProvider;
    private readonly string _connectionString;
    private bool _initialized;

    public SqliteLedgerStore(string databasePath, TimeProvider? timeProvider = null)
    {
        DatabasePath = databasePath;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!;
        Directory.CreateDirectory(directory);

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);

        await using (SqliteCommand journalModeCommand = connection.CreateCommand())
        {
            journalModeCommand.CommandText = "PRAGMA journal_mode = WAL;";
            string journalMode = (string?)await journalModeCommand.ExecuteScalarAsync(cancellationToken)
                ?? string.Empty;
            if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The database did not enable WAL journal mode. It returned '{journalMode}'.");
            }
        }

        int userVersion = await GetUserVersionAsync(connection, cancellationToken);
        int latestVersion = LedgerMigrations.Migrations[^1].Version;
        if (userVersion > latestVersion)
        {
            throw new InvalidOperationException(
                $"Database user_version {userVersion} is newer than the supported version {latestVersion}.");
        }

        foreach ((int version, string sql) in LedgerMigrations.Migrations)
        {
            if (version <= userVersion)
            {
                continue;
            }

            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using (SqliteCommand migrationCommand = connection.CreateCommand())
            {
                migrationCommand.Transaction = transaction;
                migrationCommand.CommandText = sql;
                await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (SqliteCommand versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction = transaction;
                versionCommand.CommandText = $"PRAGMA user_version = {version};";
                await versionCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            userVersion = version;
        }

        _initialized = true;
    }

    public async Task<int> RecordAsync(
        IEnumerable<LedgerEntry> entries,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        List<LedgerEntry> batch = [.. entries];
        foreach (LedgerEntry entry in batch)
        {
            ValidateEntry(entry, nameof(entries));
        }

        if (batch.Count == 0)
        {
            return 0;
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        int inserted = 0;
        long recordedAtUnixMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        foreach (LedgerEntry entry in batch)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = InsertSql;
            AddInsertParameters(command, entry, recordedAtUnixMs);
            inserted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    public async Task<IReadOnlyList<LedgerRollupRow>> GetRollupAsync(
        LedgerGrouping grouping,
        LedgerFilter filter,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = GetRollupSql(grouping);
        command.Parameters.AddWithValue("@from", OrDbNull(filter.From?.ToUnixTimeMilliseconds()));
        command.Parameters.AddWithValue("@to", OrDbNull(filter.To?.ToUnixTimeMilliseconds()));
        command.Parameters.AddWithValue("@repository", OrDbNull(filter.Repository));
        command.Parameters.AddWithValue("@branch", OrDbNull(filter.Branch));

        List<LedgerRollupRow> rows = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            decimal aiCreditsUsd =
                PicoUsd.ToUsd(reader.GetInt64(reader.GetOrdinal("github_usd_pico")));
            rows.Add(new LedgerRollupRow
            {
                Repository = grouping is LedgerGrouping.Repository or LedgerGrouping.Branch
                    ? reader.GetString(reader.GetOrdinal("repository"))
                    : null,
                Branch = grouping == LedgerGrouping.Branch
                    ? reader.GetString(reader.GetOrdinal("branch"))
                    : null,
                Model = grouping == LedgerGrouping.Model
                    ? reader.GetString(reader.GetOrdinal("model"))
                    : null,
                Agent = grouping == LedgerGrouping.Agent
                    ? reader.GetString(reader.GetOrdinal("agent"))
                    : null,
                SpanCount = reader.GetInt64(reader.GetOrdinal("span_count")),
                UnpricedSpanCount = reader.GetInt64(reader.GetOrdinal("unpriced_span_count")),
                InputTokens = reader.GetInt64(reader.GetOrdinal("input_tokens")),
                OutputTokens = reader.GetInt64(reader.GetOrdinal("output_tokens")),
                CacheReadInputTokens =
                    reader.GetInt64(reader.GetOrdinal("cache_read_input_tokens")),
                CacheWriteInputTokens =
                    reader.GetInt64(reader.GetOrdinal("cache_write_input_tokens")),
                AiCredits = AiCreditMath.UsdToAiCredits(aiCreditsUsd),
                AiCreditsUsd = aiCreditsUsd,
                FoundryByokUsd =
                    PicoUsd.ToUsd(reader.GetInt64(reader.GetOrdinal("byok_usd_pico"))),
                LocalSavedUsd =
                    PicoUsd.ToUsd(reader.GetInt64(reader.GetOrdinal("saved_usd_pico"))),
            });
        }

        return rows;
    }

    // Inserts links in one transaction and returns the number inserted. Existing (trace_id, span_id) rows are kept unchanged.
    // Throws InvalidOperationException if InitializeAsync hasn't run.
    public async Task<int> RecordSpanLinksAsync(
        IEnumerable<GenAiSpanLink> links,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        List<GenAiSpanLink> batch = [.. links];
        if (batch.Count == 0)
        {
            return 0;
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        int inserted = 0;
        long recordedAtUnixMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        foreach (GenAiSpanLink link in batch)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO span_links (
                    trace_id, span_id, parent_span_id, operation_name, agent_name,
                    conversation_id, git_repository_slug, git_branch, git_commit_sha,
                    start_time_unix_ms, recorded_at_unix_ms
                ) VALUES (
                    @trace_id, @span_id, @parent_span_id, @operation_name, @agent_name,
                    @conversation_id, @git_repository_slug, @git_branch, @git_commit_sha,
                    @start_time_unix_ms, @recorded_at_unix_ms
                ) ON CONFLICT (trace_id, span_id) DO NOTHING;
                """;
            command.Parameters.AddWithValue("@trace_id", link.TraceId);
            command.Parameters.AddWithValue("@span_id", link.SpanId);
            command.Parameters.AddWithValue("@parent_span_id", OrDbNull(link.ParentSpanId));
            command.Parameters.AddWithValue("@operation_name", OrDbNull(link.OperationName));
            command.Parameters.AddWithValue("@agent_name", OrDbNull(link.AgentName));
            command.Parameters.AddWithValue(
                "@conversation_id",
                OrDbNull(link.ConversationId));
            command.Parameters.AddWithValue(
                "@git_repository_slug",
                OrDbNull(link.GitRepositorySlug));
            command.Parameters.AddWithValue("@git_branch", OrDbNull(link.GitBranch));
            command.Parameters.AddWithValue("@git_commit_sha", OrDbNull(link.GitCommitSha));
            command.Parameters.AddWithValue(
                "@start_time_unix_ms",
                link.StartTime.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("@recorded_at_unix_ms", recordedAtUnixMs);
            inserted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    // For each trace, copies each invoke_agent link's agent name, conversation id, and Git slug/branch/sha onto spans rows below it,
    // without crossing a nested invoke_agent. Returns the total rows changed by all UPDATE statements. One transaction.
    // Throws InvalidOperationException if InitializeAsync hasn't run.
    public async Task<int> BackfillFromAgentSpansAsync(
        IEnumerable<string> traceIds,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        string[] traces = traceIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (traces.Length == 0)
        {
            return 0;
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        int changed = 0;
        foreach (string trace in traces)
        {
            List<AgentLink> agents = [];
            await using (SqliteCommand read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = """
                    SELECT span_id, agent_name, conversation_id, git_repository_slug, git_branch, git_commit_sha FROM span_links WHERE trace_id = @trace AND operation_name = 'invoke_agent' ORDER BY span_id;
                    """;
                read.Parameters.AddWithValue("@trace", trace);
                await using SqliteDataReader reader =
                    await read.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    agents.Add(new AgentLink(
                        reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5)));
                }
            }

            foreach (AgentLink agent in agents)
            {
                if (agent.AgentName is not null || agent.ConversationId is not null)
                {
                    await using SqliteCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
                        WITH RECURSIVE sub(span_id) AS (
                            SELECT span_id FROM span_links
                             WHERE trace_id = @trace AND parent_span_id = @agentSpanId
                               AND (operation_name IS NULL OR operation_name <> 'invoke_agent')
                            UNION
                            SELECT l.span_id FROM span_links AS l JOIN sub ON l.parent_span_id = sub.span_id
                             WHERE l.trace_id = @trace
                               AND (l.operation_name IS NULL OR l.operation_name <> 'invoke_agent')
                        )
                        UPDATE spans
                           SET agent_name      = COALESCE(agent_name, @agentName),
                               conversation_id = COALESCE(conversation_id, @conversationId)
                         WHERE trace_id = @trace
                           AND span_id IN (SELECT span_id FROM sub)
                           AND ((agent_name IS NULL AND @agentName IS NOT NULL)
                             OR (conversation_id IS NULL AND @conversationId IS NOT NULL));
                        """;
                    command.Parameters.AddWithValue("@trace", trace);
                    command.Parameters.AddWithValue("@agentSpanId", agent.SpanId);
                    command.Parameters.AddWithValue("@agentName", OrDbNull(agent.AgentName));
                    command.Parameters.AddWithValue(
                        "@conversationId",
                        OrDbNull(agent.ConversationId));
                    changed += await command.ExecuteNonQueryAsync(cancellationToken);
                }

                if (agent.GitRepositorySlug is not null)
                {
                    await using SqliteCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
                        WITH RECURSIVE sub(span_id) AS (
                            SELECT span_id FROM span_links
                             WHERE trace_id = @trace AND parent_span_id = @agentSpanId
                               AND (operation_name IS NULL OR operation_name <> 'invoke_agent')
                            UNION
                            SELECT l.span_id FROM span_links AS l JOIN sub ON l.parent_span_id = sub.span_id
                             WHERE l.trace_id = @trace
                               AND (l.operation_name IS NULL OR l.operation_name <> 'invoke_agent')
                        )
                        UPDATE spans
                           SET repo_slug       = @slug,
                               branch_name     = @branch,
                               head_commit_sha = @sha
                         WHERE trace_id = @trace
                           AND span_id IN (SELECT span_id FROM sub)
                           AND repo_root IS NULL AND repo_slug IS NULL;
                        """;
                    command.Parameters.AddWithValue("@trace", trace);
                    command.Parameters.AddWithValue("@agentSpanId", agent.SpanId);
                    command.Parameters.AddWithValue("@slug", agent.GitRepositorySlug);
                    command.Parameters.AddWithValue("@branch", OrDbNull(agent.GitBranch));
                    command.Parameters.AddWithValue("@sha", OrDbNull(agent.GitCommitSha));
                    changed += await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    // Deletes span_links rows whose start_time_unix_ms < startedBefore.ToUnixTimeMilliseconds(). Returns the rows deleted.
    public async Task<int> PruneSpanLinksAsync(
        DateTimeOffset startedBefore,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM span_links WHERE start_time_unix_ms < @before;
            """;
        command.Parameters.AddWithValue(
            "@before",
            startedBefore.ToUnixTimeMilliseconds());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Prompt chains (one per trace_id), most expensive first. Throws ArgumentOutOfRangeException unless 1 <= limit <= 1000.
    public async Task<IReadOnlyList<PromptChainRow>> GetTopPromptChainsAsync(
        LedgerFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        List<PromptChainData> data = [];
        await using (SqliteCommand query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT trace_id,
                       MIN(start_time_unix_ms)                                                            AS start_ms,
                       MAX(end_time_unix_ms)                                                              AS end_ms,
                       MAX(conversation_id)                                                               AS conversation_id,
                       MAX(COALESCE(repo_slug, repo_root, '(none)'))                                      AS repository,
                       MAX(COALESCE(branch_name, '(none)'))                                               AS branch,
                       COUNT(*)                                                                           AS span_count,
                       SUM(CASE WHEN pricing_status IN ('priced', 'local_offload') THEN 0 ELSE 1 END)     AS unpriced_span_count,
                       SUM(input_tokens)                                                                  AS input_tokens,
                       SUM(output_tokens)                                                                 AS output_tokens,
                       SUM(cache_read_input_tokens)                                                       AS cache_read_input_tokens,
                       SUM(cache_write_input_tokens)                                                      AS cache_write_input_tokens,
                       SUM(CASE WHEN billing_channel = 'github_ai_credits' THEN cost_usd_pico ELSE 0 END) AS github_usd_pico,
                       SUM(CASE WHEN billing_channel = 'foundry_byok' THEN cost_usd_pico ELSE 0 END)      AS byok_usd_pico,
                       SUM(saved_usd_pico)                                                                AS saved_usd_pico
                FROM spans
                WHERE (@from IS NULL OR start_time_unix_ms >= @from)
                  AND (@to IS NULL OR start_time_unix_ms < @to)
                  AND (@repository IS NULL OR COALESCE(repo_slug, repo_root, '(none)') = @repository)
                  AND (@branch IS NULL OR COALESCE(branch_name, '(none)') = @branch)
                GROUP BY trace_id
                ORDER BY github_usd_pico + byok_usd_pico DESC, saved_usd_pico DESC, start_ms DESC, trace_id ASC
                LIMIT @limit;
                """;
            query.Parameters.AddWithValue("@from", OrDbNull(filter.From?.ToUnixTimeMilliseconds()));
            query.Parameters.AddWithValue("@to", OrDbNull(filter.To?.ToUnixTimeMilliseconds()));
            query.Parameters.AddWithValue("@repository", OrDbNull(filter.Repository));
            query.Parameters.AddWithValue("@branch", OrDbNull(filter.Branch));
            query.Parameters.AddWithValue("@limit", limit);
            await using SqliteDataReader reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                data.Add(new PromptChainData(
                    reader.GetString(reader.GetOrdinal("trace_id")),
                    reader.IsDBNull(reader.GetOrdinal("conversation_id"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("conversation_id")),
                    reader.GetInt64(reader.GetOrdinal("start_ms")),
                    reader.GetInt64(reader.GetOrdinal("end_ms")),
                    reader.GetString(reader.GetOrdinal("repository")),
                    reader.GetString(reader.GetOrdinal("branch")),
                    reader.GetInt64(reader.GetOrdinal("span_count")),
                    reader.GetInt64(reader.GetOrdinal("unpriced_span_count")),
                    reader.GetInt64(reader.GetOrdinal("input_tokens")),
                    reader.GetInt64(reader.GetOrdinal("output_tokens")),
                    reader.GetInt64(reader.GetOrdinal("cache_read_input_tokens")),
                    reader.GetInt64(reader.GetOrdinal("cache_write_input_tokens")),
                    reader.GetInt64(reader.GetOrdinal("github_usd_pico")),
                    reader.GetInt64(reader.GetOrdinal("byok_usd_pico")),
                    reader.GetInt64(reader.GetOrdinal("saved_usd_pico"))));
            }
        }

        if (data.Count == 0)
        {
            return [];
        }

        Dictionary<string, List<string>> agents = data.ToDictionary(
            row => row.TraceId,
            _ => new List<string>(),
            StringComparer.Ordinal);
        Dictionary<string, List<string>> models = data.ToDictionary(
            row => row.TraceId,
            _ => new List<string>(),
            StringComparer.Ordinal);
        await using (SqliteCommand query = connection.CreateCommand())
        {
            string traceParameters = string.Join(
                ", ",
                Enumerable.Range(0, data.Count).Select(index => $"@t{index}"));
            query.CommandText = $"""
                SELECT DISTINCT trace_id, agent_name, model_id
                FROM spans
                WHERE (@from IS NULL OR start_time_unix_ms >= @from)
                  AND (@to IS NULL OR start_time_unix_ms < @to)
                  AND (@repository IS NULL OR COALESCE(repo_slug, repo_root, '(none)') = @repository)
                  AND (@branch IS NULL OR COALESCE(branch_name, '(none)') = @branch)
                  AND trace_id IN ({traceParameters});
                """;
            query.Parameters.AddWithValue("@from", OrDbNull(filter.From?.ToUnixTimeMilliseconds()));
            query.Parameters.AddWithValue("@to", OrDbNull(filter.To?.ToUnixTimeMilliseconds()));
            query.Parameters.AddWithValue("@repository", OrDbNull(filter.Repository));
            query.Parameters.AddWithValue("@branch", OrDbNull(filter.Branch));
            for (int index = 0; index < data.Count; index++)
            {
                query.Parameters.AddWithValue($"@t{index}", data[index].TraceId);
            }

            await using SqliteDataReader reader =
                await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string trace = reader.GetString(0);
                if (!reader.IsDBNull(1))
                {
                    agents[trace].Add(reader.GetString(1));
                }

                if (!reader.IsDBNull(2))
                {
                    models[trace].Add(reader.GetString(2));
                }
            }
        }

        List<PromptChainRow> rows = [];
        foreach (PromptChainData item in data)
        {
            decimal aiCreditsUsd = PicoUsd.ToUsd(item.GitHubUsdPico);
            rows.Add(new PromptChainRow
            {
                TraceId = item.TraceId,
                ConversationId = item.ConversationId,
                StartTime = DateTimeOffset.FromUnixTimeMilliseconds(item.StartMs),
                EndTime = DateTimeOffset.FromUnixTimeMilliseconds(item.EndMs),
                Repository = item.Repository,
                Branch = item.Branch,
                Agents = agents[item.TraceId].Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray(),
                Models = models[item.TraceId].Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray(),
                SpanCount = item.SpanCount,
                UnpricedSpanCount = item.UnpricedSpanCount,
                InputTokens = item.InputTokens,
                OutputTokens = item.OutputTokens,
                CacheReadInputTokens = item.CacheReadInputTokens,
                CacheWriteInputTokens = item.CacheWriteInputTokens,
                AiCredits = AiCreditMath.UsdToAiCredits(aiCreditsUsd),
                AiCreditsUsd = aiCreditsUsd,
                FoundryByokUsd = PicoUsd.ToUsd(item.ByokUsdPico),
                LocalSavedUsd = PicoUsd.ToUsd(item.SavedUsdPico),
            });
        }

        return rows;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<int> GetUserVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "InitializeAsync must complete before the ledger can be used.");
        }
    }

    private static void ValidateEntry(LedgerEntry entry, string parameterName)
    {
        NormalizedGenAiSpan span = entry.Span;
        if (entry.Calculation.Status == PricingStatus.NotBillableOperation
            || span.InputTokens < 0
            || span.OutputTokens < 0
            || span.CacheReadInputTokens < 0
            || span.CacheWriteInputTokens < 0)
        {
            throw new ArgumentException(
                "Entries must be billable and all token counts must be non-negative.",
                parameterName);
        }
    }

    private static void AddInsertParameters(
        SqliteCommand command,
        LedgerEntry entry,
        long recordedAtUnixMs)
    {
        NormalizedGenAiSpan span = entry.Span;
        CreditCalculationResult calculation = entry.Calculation;

        command.Parameters.AddWithValue("@trace_id", span.TraceId);
        command.Parameters.AddWithValue("@span_id", span.SpanId);
        command.Parameters.AddWithValue("@parent_span_id", OrDbNull(span.ParentSpanId));
        command.Parameters.AddWithValue("@operation_name", span.OperationName);
        command.Parameters.AddWithValue("@conversation_id", OrDbNull(span.ConversationId));
        command.Parameters.AddWithValue("@agent_name", OrDbNull(span.AgentName));
        command.Parameters.AddWithValue("@provider_name", OrDbNull(span.ProviderName));
        command.Parameters.AddWithValue("@request_model", OrDbNull(span.RequestModel));
        command.Parameters.AddWithValue("@response_model", OrDbNull(span.ResponseModel));
        command.Parameters.AddWithValue("@model_id", OrDbNull(calculation.ModelId));
        command.Parameters.AddWithValue("@rate_card_model_id", OrDbNull(calculation.RateCardModelId));
        command.Parameters.AddWithValue("@tier_name", OrDbNull(calculation.TierName));
        command.Parameters.AddWithValue("@server_address", OrDbNull(span.ServerAddress));
        command.Parameters.AddWithValue("@server_port", OrDbNull(span.ServerPort));
        command.Parameters.AddWithValue(
            "@start_time_unix_ms",
            span.StartTime.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue(
            "@end_time_unix_ms",
            span.EndTime.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("@input_tokens", span.InputTokens);
        command.Parameters.AddWithValue("@output_tokens", span.OutputTokens);
        command.Parameters.AddWithValue(
            "@cache_read_input_tokens",
            span.CacheReadInputTokens);
        command.Parameters.AddWithValue(
            "@cache_write_input_tokens",
            span.CacheWriteInputTokens);
        command.Parameters.AddWithValue(
            "@billing_channel",
            ToDatabaseString(calculation.Channel));
        command.Parameters.AddWithValue(
            "@pricing_status",
            ToDatabaseString(calculation.Status));
        command.Parameters.AddWithValue("@rates_version", calculation.RatesVersion);
        command.Parameters.AddWithValue(
            "@cost_usd_pico",
            PicoUsd.FromUsd(calculation.UsdCost));
        command.Parameters.AddWithValue(
            "@saved_usd_pico",
            PicoUsd.FromUsd(calculation.EstimatedCloudSavedUsd));
        command.Parameters.AddWithValue("@repo_root", OrDbNull(entry.Git?.RepositoryRoot));
        command.Parameters.AddWithValue("@repo_slug", OrDbNull(entry.Git?.RepositorySlug));
        command.Parameters.AddWithValue("@branch_name", OrDbNull(entry.Git?.BranchName));
        command.Parameters.AddWithValue(
            "@head_commit_sha",
            OrDbNull(entry.Git?.HeadCommitSha));
        command.Parameters.AddWithValue("@recorded_at_unix_ms", recordedAtUnixMs);
    }

    private static string GetRollupSql(LedgerGrouping grouping)
    {
        (string keyColumns, string keyAliases) = grouping switch
        {
            LedgerGrouping.Repository =>
                ("COALESCE(repo_slug, repo_root, '(none)') AS repository",
                 "repository"),
            LedgerGrouping.Branch =>
                ("""
                 COALESCE(repo_slug, repo_root, '(none)') AS repository,
                          COALESCE(branch_name, '(none)') AS branch
                 """,
                 "repository, branch"),
            LedgerGrouping.Model =>
                ("COALESCE(model_id, '(none)') AS model",
                 "model"),
            LedgerGrouping.Agent =>
                ("COALESCE(agent_name, '(none)') AS agent",
                 "agent"),
            _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping, null),
        };

        return $"""
            SELECT {keyColumns},
                   COUNT(*)                                                                          AS span_count,
                   SUM(CASE WHEN pricing_status IN ('priced', 'local_offload') THEN 0 ELSE 1 END)    AS unpriced_span_count,
                   SUM(input_tokens)                                                                 AS input_tokens,
                   SUM(output_tokens)                                                                AS output_tokens,
                   SUM(cache_read_input_tokens)                                                      AS cache_read_input_tokens,
                   SUM(cache_write_input_tokens)                                                     AS cache_write_input_tokens,
                   SUM(CASE WHEN billing_channel = 'github_ai_credits' THEN cost_usd_pico ELSE 0 END) AS github_usd_pico,
                   SUM(CASE WHEN billing_channel = 'foundry_byok' THEN cost_usd_pico ELSE 0 END)      AS byok_usd_pico,
                   SUM(saved_usd_pico)                                                               AS saved_usd_pico
            FROM spans
            WHERE (@from IS NULL OR start_time_unix_ms >= @from)
              AND (@to IS NULL OR start_time_unix_ms < @to)
              AND (@repository IS NULL OR COALESCE(repo_slug, repo_root, '(none)') = @repository)
              AND (@branch IS NULL OR COALESCE(branch_name, '(none)') = @branch)
            GROUP BY {keyAliases}
            ORDER BY github_usd_pico + byok_usd_pico DESC, saved_usd_pico DESC, {keyAliases} ASC;
            """;
    }

    private static string ToDatabaseString(BillingChannel channel) => channel switch
    {
        BillingChannel.GitHubAiCredits => "github_ai_credits",
        BillingChannel.FoundryByok => "foundry_byok",
        BillingChannel.Local => "local",
        BillingChannel.Unpriced => "unpriced",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null),
    };

    private static string ToDatabaseString(PricingStatus status) => status switch
    {
        PricingStatus.Priced => "priced",
        PricingStatus.LocalOffload => "local_offload",
        PricingStatus.ByokUnpriced => "byok_unpriced",
        PricingStatus.UnknownModel => "unknown_model",
        PricingStatus.NoRateForDate => "no_rate_for_date",
        PricingStatus.UnrecognizedEndpoint => "unrecognized_endpoint",
        PricingStatus.InvalidUsage => "invalid_usage",
        PricingStatus.NotBillableOperation => "not_billable_operation",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static object OrDbNull<T>(T? value) where T : struct =>
        value.HasValue ? value.Value : DBNull.Value;

    private static object OrDbNull(string? value) => value is null ? DBNull.Value : value;

    private sealed record AgentLink(
        string SpanId,
        string? AgentName,
        string? ConversationId,
        string? GitRepositorySlug,
        string? GitBranch,
        string? GitCommitSha);

    private sealed record PromptChainData(
        string TraceId,
        string? ConversationId,
        long StartMs,
        long EndMs,
        string Repository,
        string Branch,
        long SpanCount,
        long UnpricedSpanCount,
        long InputTokens,
        long OutputTokens,
        long CacheReadInputTokens,
        long CacheWriteInputTokens,
        long GitHubUsdPico,
        long ByokUsdPico,
        long SavedUsdPico);
}
