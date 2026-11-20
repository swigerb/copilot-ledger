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

}
