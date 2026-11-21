using System.Globalization;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using CopilotLedger.Cli;
using CopilotLedger.Core.Breaker;
using CopilotLedger.Core.Git;
using CopilotLedger.Core.Pricing;
using CopilotLedger.Core.Telemetry;
using CopilotLedger.Mcp;
using CopilotLedger.Storage;
using CopilotLedger.Tests.TestSupport;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CopilotLedger.Tests.Mcp;

public sealed class LedgerMcpServerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-11-21T12:00:00Z", CultureInfo.InvariantCulture);
    private const string T5 = "55555555555555555555555555555555";
    private const string T6 = "66666666666666666666666666666666";
    private const string Hash =
        "c3d1fe831856c77818bb8c1242d32d6efa6c37366eb902e56c5fd0054c1dcbc3/918a44e755cc5eee09b08d214831e8339a645158ef1f831a0fa02bd43999700f";
    private const string BranchSchema =
        """
        {
          "type": "object",
          "properties": {
            "repository": { "type": "string", "minLength": 1, "description": "Repository key: owner/repo, a repository root, or (none). Pass with branch." },
            "branch": { "type": "string", "minLength": 1, "description": "Branch name, or (none). Pass with repository." }
          },
          "additionalProperties": false
        }
        """;
    private const string PromptSchema =
        """
        {
          "type": "object",
          "properties": {
            "repository": { "type": "string", "minLength": 1, "description": "Repository key: owner/repo, a repository root, or (none). Pass with branch." },
            "branch": { "type": "string", "minLength": 1, "description": "Branch name, or (none). Pass with repository." },
            "limit": { "type": "integer", "minimum": 1, "maximum": 20, "default": 5, "description": "Number of prompt chains." },
            "orderBy": { "type": "string", "enum": ["cost", "outputTokens", "failedToolCalls"], "default": "cost", "description": "Sort key, highest first." }
          },
          "additionalProperties": false
        }
        """;
    private const string BudgetSchema =
        """
        {
          "type": "object",
          "properties": {
            "repository": { "type": "string", "minLength": 1, "description": "Repository key: owner/repo, a repository root, or (none). Pass with branch." },
            "branch": { "type": "string", "minLength": 1, "description": "Branch name, or (none). Pass with repository." },
            "aiCredits": { "type": "number", "exclusiveMinimum": 0, "maximum": 1000000, "description": "Budget in AI credits (1 AI credit = $0.01), with at most 2 decimal places." }
          },
          "required": ["aiCredits"],
          "additionalProperties": false
        }
        """;
    private const string Branch =
        """
        {
          "repository": "swigerb/copilot-ledger",
          "branch": "feature/ledger",
          "spans": 4,
          "unpricedSpans": 0,
          "inputTokens": 1510000,
          "outputTokens": 362000,
          "cacheReadInputTokens": 400000,
          "cacheWriteInputTokens": 0,
          "aiCredits": 176.5,
          "aiCreditsUsd": 1.765,
          "foundryByokUsd": 0,
          "localSavedUsd": 0.23,
          "billedAiCredits": 176.5,
          "topModel": "mai-code-2",
          "budget": null,
          "circuitBreaker": { "state": "closed", "openTrips": [] },
          "warnings": []
        }
        """;

    [Fact]
    public async Task MC1_ToolsList()
    {
        using TempDirectory temp = new();
        GitFixture.Create(temp.Path);
        Dictionary<string, string> env = [];
        await using McpTestHarness harness = await StartAsync(
            Context(temp.Path, env));

        Implementation? serverInfo = harness.Client.ServerInfo;
        Assert.NotNull(serverInfo);
        Assert.Equal("copilot-ledger", serverInfo.Name);
        Assert.Equal("9.9.9", serverInfo.Version);
        Assert.Equal(
            LedgerMcpServer.Instructions,
            harness.Client.ServerInstructions);
        IList<McpClientTool> tools = await harness.Client.ListToolsAsync();
        Assert.Collection(
            tools,
            tool => AssertTool(
                tool,
                "get_branch_ledger",
                "Branch ledger",
                "Spend on one Git branch: AI credits, USD, BYOK USD, local savings, top model, budget, and circuit-breaker status. Uses the current repository and branch unless you pass both.",
                BranchSchema,
                readOnly: true,
                destructive: null,
                idempotent: null),
            tool => AssertTool(
                tool,
                "get_top_expensive_prompts",
                "Top expensive prompts",
                "The most expensive prompt chains on one Git branch, with their output tokens and failed tool calls (retry loops). Uses the current repository and branch unless you pass both. Returns 5 by default, ordered by cost.",
                PromptSchema,
                readOnly: true,
                destructive: null,
                idempotent: null),
            tool => AssertTool(
                tool,
                "get_agent_breakdown",
                "Agent breakdown",
                "Spend on one Git branch split by agent (gen_ai.agent.name). Uses the current repository and branch unless you pass both.",
                BranchSchema,
                readOnly: true,
                destructive: null,
                idempotent: null),
            tool => AssertTool(
                tool,
                "set_branch_budget",
                "Set branch budget",
                "Sets a soft AI-credit budget for one Git branch. CopilotLedger alerts once when the branch's billed spend exceeds it; nothing is blocked. Uses the current repository and branch unless you pass both.",
                BudgetSchema,
                readOnly: false,
                destructive: false,
                idempotent: true));
    }

    [Fact]
    public async Task MC2_TheBranchLedger()
    {
        using TempDirectory temp = new();
        GitFixture.Create(temp.Path);
        await SeedAsync(temp.Path);
        await using McpTestHarness harness = await StartAsync(
            Context(temp.Path, []));
        await AssertResultAsync(
            await CallAsync(harness, "get_branch_ledger", "{}"),
            Branch);
    }

    [Fact]
    public async Task MC3_Warnings()
    {
        using TempDirectory temp = new();
        GitFixture.Create(temp.Path);
        await SeedAsync(temp.Path);
        SqliteLedgerStore store = Store(temp.Path);
        await store.InitializeAsync();
        await store.RecordBreakerTripAsync(new NewBreakerTrip
        {
            Rule = BreakerRule.RetryChurn,
            TraceId = T5,
            ConversationId = "conv-5",
            Repository = "swigerb/copilot-ledger",
            Branch = "feature/ledger",
            RepositoryRoot = Path.Combine(temp.Path, "repo"),
            TrippedAt = DateTimeOffset.Parse(
                "2026-11-21T15:00:05Z",
                CultureInfo.InvariantCulture),
            Detail = "5 failed tool calls in a row",
            FailureCount = 5,
        });
        await store.RecordBreakerTripAsync(new NewBreakerTrip
        {
            Rule = BreakerRule.RetryChurn,
            TraceId = new string('a', 32),
            ConversationId = "conv-other",
            Repository = "contoso/api",
            Branch = "main",
            TrippedAt = DateTimeOffset.Parse(
                "2026-11-21T09:00:00Z",
                CultureInfo.InvariantCulture),
            Detail = "7 failed tool calls in a row",
            FailureCount = 7,
        });
        await store.RecordBreakerTripAsync(new NewBreakerTrip
        {
            Rule = BreakerRule.RetryChurn,
            TraceId = new string('b', 32),
            ConversationId = "conv-5",
            Repository = "swigerb/copilot-ledger",
            Branch = "main",
            TrippedAt = DateTimeOffset.Parse(
                "2026-11-21T14:30:00Z",
                CultureInfo.InvariantCulture),
            Detail = "6 failed tool calls in a row",
            FailureCount = 6,
        });
        await store.SetBranchBudgetAsync(
            "swigerb/copilot-ledger",
            "feature/ledger",
            150m);
        Dictionary<string, string> env = [];
        await using (McpTestHarness harness = await StartAsync(
            Context(temp.Path, env)))
        {
            JsonObject expected = JsonNode.Parse(Branch)!.AsObject();
            expected["budget"] = JsonNode.Parse(
                """{"aiCredits":150,"spentAiCredits":176.5,"exceeded":true}""");
            expected["circuitBreaker"] = JsonNode.Parse(
                """{"state":"open","openTrips":[{"id":1,"rule":"retry_churn","repository":"swigerb/copilot-ledger","branch":"feature/ledger","traceId":"55555555555555555555555555555555","conversationId":"conv-5","currentSession":false,"trippedAt":"2026-11-21T15:00:05.0000000+00:00","detail":"5 failed tool calls in a row","resetCommand":"gh-ledger breaker reset --id 1"}]}""");
            expected["warnings"] = JsonNode.Parse(
                """["WARNING: CopilotLedger circuit breaker is open for swigerb/copilot-ledger @ feature/ledger: 5 failed tool calls in a row on one prompt (trip 1). Stop and ask the user before continuing. The user can reset it with: gh-ledger breaker reset --id 1","WARNING: swigerb/copilot-ledger @ feature/ledger has spent 176.50 of its 150.00 AI credit budget."]""");
            await AssertResultAsync(
                await CallAsync(harness, "get_branch_ledger", "{}"),
                expected);
        }

        env["COPILOT_AGENT_SESSION_ID"] = "conv-5";
        await using McpTestHarness session = await StartAsync(
            Context(temp.Path, env));
        JsonObject expectedSession = JsonNode.Parse(Branch)!.AsObject();
        expectedSession["budget"] = JsonNode.Parse(
            """{"aiCredits":150,"spentAiCredits":176.5,"exceeded":true}""");
        expectedSession["circuitBreaker"] = JsonNode.Parse(
            """{"state":"open","openTrips":[{"id":1,"rule":"retry_churn","repository":"swigerb/copilot-ledger","branch":"feature/ledger","traceId":"55555555555555555555555555555555","conversationId":"conv-5","currentSession":true,"trippedAt":"2026-11-21T15:00:05.0000000+00:00","detail":"5 failed tool calls in a row","resetCommand":"gh-ledger breaker reset --id 1"},{"id":3,"rule":"retry_churn","repository":"swigerb/copilot-ledger","branch":"main","traceId":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","conversationId":"conv-5","currentSession":true,"trippedAt":"2026-11-21T14:30:00.0000000+00:00","detail":"6 failed tool calls in a row","resetCommand":"gh-ledger breaker reset --id 3"}]}""");
        expectedSession["warnings"] = JsonNode.Parse(
            """["WARNING: CopilotLedger circuit breaker is open for swigerb/copilot-ledger @ feature/ledger: 5 failed tool calls in a row on one prompt (trip 1). Stop and ask the user before continuing. The user can reset it with: gh-ledger breaker reset --id 1","WARNING: CopilotLedger circuit breaker is open for swigerb/copilot-ledger @ main: 6 failed tool calls in a row on one prompt (trip 3). Stop and ask the user before continuing. The user can reset it with: gh-ledger breaker reset --id 3","WARNING: swigerb/copilot-ledger @ feature/ledger has spent 176.50 of its 150.00 AI credit budget."]""");
        await AssertResultAsync(
            await CallAsync(session, "get_branch_ledger", "{}"),
            expectedSession);
    }

    [Fact]
    public async Task MC4_AnExplicitScope()
    {
        using TempDirectory temp = new();
        GitFixture.Create(temp.Path);
        await SeedAsync(temp.Path);
        await using McpTestHarness harness = await StartAsync(
            Context(temp.Path, []));
        await AssertResultAsync(
            await CallAsync(
                harness,
                "get_branch_ledger",
                """{"repository":"contoso/api","branch":"main"}"""),
            """
            {"repository":"contoso/api","branch":"main","spans":1,"unpricedSpans":0,"inputTokens":100000,"outputTokens":10000,"cacheReadInputTokens":0,"cacheWriteInputTokens":0,"aiCredits":0,"aiCreditsUsd":0,"foundryByokUsd":0.315,"localSavedUsd":0,"billedAiCredits":31.5,"topModel":"gpt-6.1-sol","budget":null,"circuitBreaker":{"state":"closed","openTrips":[]},"warnings":[]}
            """);
        await AssertResultAsync(
            await CallAsync(
                harness,
                "get_branch_ledger",
                """{"repository":"swigerb/copilot-ledger","branch":"nope"}"""),
            """
            {"repository":"swigerb/copilot-ledger","branch":"nope","spans":0,"unpricedSpans":0,"inputTokens":0,"outputTokens":0,"cacheReadInputTokens":0,"cacheWriteInputTokens":0,"aiCredits":0,"aiCreditsUsd":0,"foundryByokUsd":0,"localSavedUsd":0,"billedAiCredits":0,"topModel":null,"budget":null,"circuitBreaker":{"state":"closed","openTrips":[]},"warnings":[]}
            """);
    }

    [Fact]
    public async Task MC5_TopPrompts()
    {
        using TempDirectory temp = new();
        GitFixture.Create(temp.Path);
        await SeedAsync(temp.Path);
        await using McpTestHarness harness = await StartAsync(
            Context(temp.Path, []));
        await AssertResultAsync(
            await CallAsync(harness, "get_top_expensive_prompts", "{}"),
            TopPrompts("cost", 5, [Prompt5(1), Prompt2(2), Prompt6(3)]));
        await AssertResultAsync(
            await CallAsync(
                harness,
                "get_top_expensive_prompts",
                """{"orderBy":"outputTokens","limit":2}"""),
            TopPrompts("outputTokens", 2, [Prompt6(1), Prompt2(2)]));
        await AssertResultAsync(
            await CallAsync(
                harness,
                "get_top_expensive_prompts",
                """{"orderBy":"failedToolCalls"}"""),
            TopPrompts("failedToolCalls", 5, [Prompt5(1), Prompt6(2), Prompt2(3)]));
    }

    [Fact]
    public async Task MC6_Agents()
    {
        using TempDirectory temp = new();
        GitFixture.Create(temp.Path);
        await SeedAsync(temp.Path);
        await using McpTestHarness harness = await StartAsync(
            Context(temp.Path, []));
        await AssertResultAsync(
            await CallAsync(harness, "get_agent_breakdown", "{}"),
            """
            {"repository":"swigerb/copilot-ledger","branch":"feature/ledger","agents":[{"agent":"(none)","spans":2,"unpricedSpans":0,"inputTokens":110000,"outputTokens":250000,"cacheReadInputTokens":0,"cacheWriteInputTokens":0,"aiCredits":120.5,"aiCreditsUsd":1.205,"foundryByokUsd":0,"localSavedUsd":0,"billedAiCredits":120.5},{"agent":"ledger-builder","spans":2,"unpricedSpans":0,"inputTokens":1400000,"outputTokens":112000,"cacheReadInputTokens":400000,"cacheWriteInputTokens":0,"aiCredits":56,"aiCreditsUsd":0.56,"foundryByokUsd":0,"localSavedUsd":0.23,"billedAiCredits":56}],"circuitBreaker":{"state":"closed","openTrips":[]},"warnings":[]}
            """);
    }

    [Fact]
    public async Task MC7_SettingABudget()
    {
        using TempDirectory temp = new();
        GitFixture.Create(temp.Path);
        await SeedAsync(temp.Path);
        await using McpTestHarness harness = await StartAsync(
            Context(temp.Path, []));
        await AssertResultAsync(
            await CallAsync(
                harness,
                "set_branch_budget",
                """{"aiCredits":200}"""),
            BudgetResult(200m, false));
        SqliteLedgerStore store = Store(temp.Path);
        await store.InitializeAsync();
        BranchBudgetRow row = Assert.Single(
            await store.GetBranchBudgetsAsync());
        Assert.Equal("swigerb/copilot-ledger", row.Repository);
        Assert.Equal("feature/ledger", row.Branch);
        Assert.Equal(200m, row.BudgetAiCredits);
        Assert.Equal(Now, row.SetAt);

        await AssertResultAsync(
            await CallAsync(
                harness,
                "set_branch_budget",
                """{"aiCredits":150.5,"repository":"swigerb/copilot-ledger","branch":"feature/ledger"}"""),
            BudgetResult(
                150.5m,
                true,
                "WARNING: swigerb/copilot-ledger @ feature/ledger has spent 176.50 of its 150.50 AI credit budget."));
        await AssertResultAsync(
            await CallAsync(
                harness,
                "set_branch_budget",
                """{"aiCredits":176.5}"""),
            BudgetResult(176.5m, false));
        await AssertResultAsync(
            await CallToolAsync(
                harness,
                "set_branch_budget",
                JsonDocument.Parse("1e2").RootElement),
            BudgetResult(100m, true,
                "WARNING: swigerb/copilot-ledger @ feature/ledger has spent 176.50 of its 100.00 AI credit budget."));
    }

    public static TheoryData<string, string, string> InvalidArguments => new()
    {
        { "get_branch_ledger", """{"repository":"a/b"}""", "Pass repository and branch together, or neither." },
        { "get_branch_ledger", """{"branch":"main"}""", "Pass repository and branch together, or neither." },
        { "get_branch_ledger", """{"repository":"","branch":"main"}""", "repository must be a non-empty string." },
        { "get_branch_ledger", """{"repository":"   ","branch":"main"}""", "repository must be a non-empty string." },
        { "get_branch_ledger", """{"repository":"a/b","branch":5}""", "branch must be a non-empty string." },
        { "get_branch_ledger", """{"extra":1,"another":2}""", "Unknown argument: another." },
        { "get_agent_breakdown", """{"limit":5}""", "Unknown argument: limit." },
        { "get_top_expensive_prompts", """{"limit":0}""", "limit must be an integer from 1 to 20." },
        { "get_top_expensive_prompts", """{"limit":21}""", "limit must be an integer from 1 to 20." },
        { "get_top_expensive_prompts", """{"limit":5.5}""", "limit must be an integer from 1 to 20." },
        { "get_top_expensive_prompts", """{"limit":"5"}""", "limit must be an integer from 1 to 20." },
        { "get_top_expensive_prompts", """{"orderBy":"Cost"}""", "orderBy must be cost, outputTokens, or failedToolCalls." },
        { "get_top_expensive_prompts", """{"orderBy":null}""", "orderBy must be cost, outputTokens, or failedToolCalls." },
        { "set_branch_budget", "{}", "aiCredits is required." },
        { "set_branch_budget", """{"aiCredits":0}""", "aiCredits must be a number greater than 0 and at most 1000000, with at most 2 decimal places." },
        { "set_branch_budget", """{"aiCredits":-1}""", "aiCredits must be a number greater than 0 and at most 1000000, with at most 2 decimal places." },
        { "set_branch_budget", """{"aiCredits":1000000.01}""", "aiCredits must be a number greater than 0 and at most 1000000, with at most 2 decimal places." },
        { "set_branch_budget", """{"aiCredits":0.001}""", "aiCredits must be a number greater than 0 and at most 1000000, with at most 2 decimal places." },
        { "set_branch_budget", """{"aiCredits":"5"}""", "aiCredits must be a number greater than 0 and at most 1000000, with at most 2 decimal places." },
        { "set_branch_budget", """{"aiCredits":5,"repository":"a/b"}""", "Pass repository and branch together, or neither." },
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task MC8_InvalidArguments(
        string tool,
        string arguments,
        string message)
    {
        using TempDirectory temp = new();
        GitFixture.Create(temp.Path);
        await using McpTestHarness harness = await StartAsync(
            Context(temp.Path, []));
        CallToolResult result = await CallAsync(harness, tool, arguments);
        Assert.True(result.IsError);
        TextContentBlock content = Assert.IsType<TextContentBlock>(
            Assert.Single(result.Content));
        Assert.Equal(message, content.Text);
        Assert.Null(result.StructuredContent);
        if (tool == "set_branch_budget")
        {
            SqliteLedgerStore store = Store(temp.Path);
            await store.InitializeAsync();
            Assert.Empty(await store.GetBranchBudgetsAsync());
        }
    }

    [Fact]
    public async Task MC9_FindingTheBranch()
    {
        using TempDirectory temp = new();
        GitFixture.Create(temp.Path);
        await SeedAsync(temp.Path);
        Dictionary<string, string> env = [];
        LedgerMcpContext context = Context(temp.Path, env) with
        {
            WorkingDirectory = temp.Path,
        };
        await using (McpTestHarness missing = await StartAsync(context))
        {
            CallToolResult result = await CallAsync(
                missing,
                "get_branch_ledger",
                "{}");
            Assert.True(result.IsError);
            Assert.Equal(
                $"No Git repository at {temp.Path}. Pass repository and branch.",
                Text(result));
            Assert.Null(result.StructuredContent);
        }

        env["COPILOT_SESSION_CWD"] = Path.Combine(temp.Path, "repo");
        await using (McpTestHarness absolute = await StartAsync(context))
        {
            await AssertResultAsync(
                await CallAsync(absolute, "get_branch_ledger", "{}"),
                Branch);
        }

        env["COPILOT_SESSION_CWD"] = "repo";
        await using (McpTestHarness relative = await StartAsync(context))
        {
            CallToolResult result = await CallAsync(
                relative,
                "get_branch_ledger",
                "{}");
            Assert.True(result.IsError);
            Assert.Equal(
                $"No Git repository at {temp.Path}. Pass repository and branch.",
                Text(result));
        }

        env["COPILOT_SESSION_CWD"] = Path.Combine(temp.Path, "missing");
        await using McpTestHarness unavailable = await StartAsync(context);
        CallToolResult missingResult = await CallAsync(
            unavailable,
            "get_branch_ledger",
            "{}");
        Assert.True(missingResult.IsError);
        Assert.Equal(
            $"No Git repository at {temp.Path}. Pass repository and branch.",
            Text(missingResult));
    }

    [Fact]
    public async Task MC10_AnUnknownTool()
    {
        using TempDirectory temp = new();
        GitFixture.Create(temp.Path);
        await using McpTestHarness harness = await StartAsync(
            Context(temp.Path, []));
        McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(
            () => harness.Client.CallToolAsync("nope", null).AsTask());
        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Contains("Unknown tool: nope", exception.Message);
    }

    [Fact]
    public async Task MC11_TheMcpCommand()
    {
        using TempDirectory temp = new();
        GitFixture.Create(temp.Path);
        await SeedAsync(temp.Path);
        await using CliHarness cli = new(
            temp.Path,
            timeProvider: new ManualTimeProvider(Now));
        Pipe toServer = new();
        Pipe toClient = new();
        StringWriter output = new(CultureInfo.InvariantCulture);
        StringWriter error = new(CultureInfo.InvariantCulture);
        Task<int> command = GhLedgerCli.InvokeAsync(
            ["mcp"],
            cli.Context with
            {
                StandardInput = toServer.Reader.AsStream(),
                StandardOutput = toClient.Writer.AsStream(),
                WorkingDirectory = Path.Combine(temp.Path, "repo"),
                Out = output,
                Error = error,
            });
        await using McpClient client = await McpClient.CreateAsync(
            new StreamClientTransport(
                toServer.Writer.AsStream(),
                toClient.Reader.AsStream()),
            new McpClientOptions
            {
                ClientInfo = new Implementation
                {
                    Name = "ledger-tests",
                    Version = "1.0",
                },
            });
        Assert.Equal(4, (await client.ListToolsAsync()).Count);
        Implementation? serverInfo = client.ServerInfo;
        Assert.NotNull(serverInfo);
        Assert.Equal(GhLedgerCli.Version, serverInfo.Version);
        using JsonDocument arguments = JsonDocument.Parse("{}");
        CallToolResult result = await client.CallToolAsync(
            "get_branch_ledger",
            arguments.RootElement.EnumerateObject().ToDictionary(
                property => property.Name,
                property => (object?)property.Value.Clone()));
        JsonElement structured = Assert.NotNull(result.StructuredContent);
        Assert.Equal(
            "swigerb/copilot-ledger",
            structured.GetProperty("repository").GetString());
        Assert.Equal(
            "feature/ledger",
            structured.GetProperty("branch").GetString());
        await client.DisposeAsync();
        await toServer.Writer.CompleteAsync();
        Assert.Equal(0, await command.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Empty(output.ToString());
        await toClient.Reader.CompleteAsync();
    }

    [Fact]
    public async Task MC12_AnUnavailableLedger()
    {
        using TempDirectory temp = new();
        GitFixture.Create(temp.Path);
        string databasePath = Path.Combine(temp.Path, "dbdir");
        Directory.CreateDirectory(databasePath);
        await using McpTestHarness harness = await StartAsync(
            Context(temp.Path, []) with { DatabasePath = databasePath });
        CallToolResult result = await CallAsync(
            harness,
            "get_branch_ledger",
            """{"repository":"a/b","branch":"main"}""");
        Assert.True(result.IsError);
        Assert.StartsWith(
            $"The ledger at {databasePath} is unavailable: ",
            Text(result));
    }

    private static async Task<McpTestHarness> StartAsync(
        LedgerMcpContext context) => await McpTestHarness.StartAsync(context);

    private static LedgerMcpContext Context(
        string root,
        Dictionary<string, string> env) => new()
    {
        DatabasePath = Path.Combine(root, "data", "ledger.db"),
        WorkingDirectory = Path.Combine(root, "repo"),
        GetEnvironmentVariable = name => env.GetValueOrDefault(name),
        GitResolver = new FileSystemGitContextResolver(ceilingDirectory: root),
        TimeProvider = new ManualTimeProvider(Now),
        Version = "9.9.9",
    };

    private static SqliteLedgerStore Store(string root) =>
        new(Path.Combine(root, "data", "ledger.db"), new ManualTimeProvider(Now));

    private static async Task SeedAsync(string root)
    {
        SqliteLedgerStore store = Store(root);
        await store.InitializeAsync();
        await LedgerSeeds.SeedChainsAsync(
            store,
            Path.Combine(root, "repo"),
            Path.Combine(root, "api"));
        JsonRateCardCalculator rates = JsonRateCardCalculator.LoadEmbedded();
        NormalizedGenAiSpan t5 = TestSpans.Chat(
            startTime: DateTimeOffset.Parse(
                "2026-11-21T15:00:00Z",
                CultureInfo.InvariantCulture),
            requestModel: "claude-sonnet-5.5",
            conversationId: "conv-5",
            inputTokens: 100_000,
            outputTokens: 50_000) with
        {
            TraceId = T5,
            SpanId = "5555555555555555",
        };
        NormalizedGenAiSpan t6 = TestSpans.Chat(
            startTime: DateTimeOffset.Parse(
                "2026-11-21T16:00:00Z",
                CultureInfo.InvariantCulture),
            requestModel: "mai-code-2",
            conversationId: "conv-6",
            inputTokens: 10_000,
            outputTokens: 200_000) with
        {
            TraceId = T6,
            SpanId = "6666666666666666",
        };
        await store.RecordAsync([
            new LedgerEntry(t5, rates.Calculate(t5), GitFixture.Feature(root)),
            new LedgerEntry(t6, rates.Calculate(t6), GitFixture.Feature(root)),
        ]);
        List<GenAiSpanLink> links = [];
        for (int index = 1; index <= 5; index++)
        {
            links.Add(ToolLink(
                T5,
                $"55555555555555a{index}",
                t5.StartTime.AddSeconds(index),
                "powershell",
                1,
                0));
        }

        links.Add(ToolLink(
            T5,
            "55555555555555a6",
            t5.StartTime.AddSeconds(6),
            "powershell",
            0,
            0));
        links.Add(ToolLink(
            T6,
            "66666666666666a1",
            t6.StartTime.AddSeconds(1),
            Hash,
            null,
            2));
        await store.RecordSpanLinksAsync(links);
    }

    private static GenAiSpanLink ToolLink(
        string trace,
        string span,
        DateTimeOffset start,
        string tool,
        long? exitCode,
        int status) => new()
    {
        TraceId = trace,
        SpanId = span,
        OperationName = "execute_tool",
        StartTime = start,
        ToolName = tool,
        ToolExitCode = exitCode,
        StatusCode = status,
    };

    private static async Task<CallToolResult> CallAsync(
        McpTestHarness harness,
        string tool,
        string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Dictionary<string, object?> arguments = document.RootElement
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => (object?)property.Value.Clone());
        return await harness.Client.CallToolAsync(tool, arguments);
    }

    private static async Task<CallToolResult> CallToolAsync(
        McpTestHarness harness,
        string tool,
        JsonElement aiCredits) =>
        await harness.Client.CallToolAsync(
            tool,
            new Dictionary<string, object?> { ["aiCredits"] = aiCredits.Clone() });

    private static string Text(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static async Task AssertResultAsync(
        CallToolResult result,
        string expected) =>
        await AssertResultAsync(result, JsonNode.Parse(expected)!);

    private static Task AssertResultAsync(
        CallToolResult result,
        JsonNode expected)
    {
        Assert.NotEqual(true, result.IsError);
        TextContentBlock content = Assert.IsType<TextContentBlock>(
            Assert.Single(result.Content));
        string[] lines = content.Text.Split('\n');
        JsonElement structured = Assert.NotNull(result.StructuredContent);
        JsonNode expectedNode = expected.DeepClone();
        Assert.True(JsonNode.DeepEquals(
            expectedNode,
            JsonNode.Parse(lines[^1])));
        Assert.True(JsonNode.DeepEquals(
            expectedNode,
            JsonNode.Parse(structured.GetRawText())));
        string[] expectedWarnings = expectedNode["warnings"]!
            .AsArray()
            .Select(node => node!.GetValue<string>())
            .ToArray();
        Assert.Equal(expectedWarnings, lines[..^1]);
        return Task.CompletedTask;
    }

    private static void AssertTool(
        McpClientTool clientTool,
        string name,
        string title,
        string description,
        string schema,
        bool readOnly,
        bool? destructive,
        bool? idempotent)
    {
        Tool tool = clientTool.ProtocolTool;
        Assert.Equal(name, clientTool.Name);
        Assert.Equal(title, tool.Title);
        Assert.Equal(description, tool.Description);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(schema),
            JsonNode.Parse(tool.InputSchema.GetRawText())));
        Assert.Null(tool.OutputSchema);
        ToolAnnotations? annotations = tool.Annotations;
        Assert.NotNull(annotations);
        Assert.Equal(readOnly, annotations.ReadOnlyHint);
        Assert.Equal(destructive, annotations.DestructiveHint);
        Assert.Equal(idempotent, annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    private static JsonObject TopPrompts(
        string orderBy,
        int limit,
        IReadOnlyList<JsonObject> prompts) => new()
    {
        ["repository"] = "swigerb/copilot-ledger",
        ["branch"] = "feature/ledger",
        ["orderBy"] = orderBy,
        ["limit"] = limit,
        ["prompts"] = new JsonArray(
            prompts.Select(node => (JsonNode)node).ToArray()),
        ["circuitBreaker"] = JsonNode.Parse(
            """{"state":"closed","openTrips":[]}"""),
        ["warnings"] = new JsonArray(),
    };

    private static JsonObject Prompt5(int rank) => new()
    {
        ["rank"] = rank,
        ["traceId"] = T5,
        ["conversationId"] = "conv-5",
        ["startTime"] = "2026-11-21T15:00:00.0000000+00:00",
        ["endTime"] = "2026-11-21T15:00:02.0000000+00:00",
        ["agents"] = new JsonArray(),
        ["models"] = new JsonArray("claude-sonnet-5.5"),
        ["spans"] = 1,
        ["unpricedSpans"] = 0,
        ["inputTokens"] = 100000,
        ["outputTokens"] = 50000,
        ["cacheReadInputTokens"] = 0,
        ["cacheWriteInputTokens"] = 0,
        ["aiCredits"] = 70,
        ["aiCreditsUsd"] = 0.7m,
        ["foundryByokUsd"] = 0,
        ["localSavedUsd"] = 0,
        ["billedAiCredits"] = 70,
        ["failedToolCalls"] = 5,
    };

    private static JsonObject Prompt2(int rank) => new()
    {
        ["rank"] = rank,
        ["traceId"] = "22222222222222222222222222222222",
        ["conversationId"] = "conv-2",
        ["startTime"] = "2026-11-21T13:00:00.0000000+00:00",
        ["endTime"] = "2026-11-21T13:05:02.0000000+00:00",
        ["agents"] = new JsonArray("ledger-builder"),
        ["models"] = new JsonArray("mai-code-2", "qwen3.8-27b"),
        ["spans"] = 2,
        ["unpricedSpans"] = 0,
        ["inputTokens"] = 1400000,
        ["outputTokens"] = 112000,
        ["cacheReadInputTokens"] = 400000,
        ["cacheWriteInputTokens"] = 0,
        ["aiCredits"] = 56,
        ["aiCreditsUsd"] = 0.56m,
        ["foundryByokUsd"] = 0,
        ["localSavedUsd"] = 0.23m,
        ["billedAiCredits"] = 56,
        ["failedToolCalls"] = 0,
    };

    private static JsonObject Prompt6(int rank) => new()
    {
        ["rank"] = rank,
        ["traceId"] = T6,
        ["conversationId"] = "conv-6",
        ["startTime"] = "2026-11-21T16:00:00.0000000+00:00",
        ["endTime"] = "2026-11-21T16:00:02.0000000+00:00",
        ["agents"] = new JsonArray(),
        ["models"] = new JsonArray("mai-code-2"),
        ["spans"] = 1,
        ["unpricedSpans"] = 0,
        ["inputTokens"] = 10000,
        ["outputTokens"] = 200000,
        ["cacheReadInputTokens"] = 0,
        ["cacheWriteInputTokens"] = 0,
        ["aiCredits"] = 50.5m,
        ["aiCreditsUsd"] = 0.505m,
        ["foundryByokUsd"] = 0,
        ["localSavedUsd"] = 0,
        ["billedAiCredits"] = 50.5m,
        ["failedToolCalls"] = 1,
    };

    private static JsonObject BudgetResult(
        decimal credits,
        bool exceeded,
        string? warning = null) => new()
    {
        ["repository"] = "swigerb/copilot-ledger",
        ["branch"] = "feature/ledger",
        ["budget"] = new JsonObject
        {
            ["aiCredits"] = credits,
            ["spentAiCredits"] = 176.5m,
            ["exceeded"] = exceeded,
        },
        ["circuitBreaker"] = JsonNode.Parse(
            """{"state":"closed","openTrips":[]}"""),
        ["warnings"] = warning is null
            ? new JsonArray()
            : new JsonArray(warning),
    };
}
