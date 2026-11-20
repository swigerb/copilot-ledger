namespace CopilotLedger.Core.Telemetry;

// One received span's place in its trace, used to inherit agent, conversation, and Git attributes from invoke_agent ancestors.
public sealed record GenAiSpanLink
{
    public required string TraceId { get; init; }             // 32 lowercase hex chars
    public required string SpanId { get; init; }              // 16 lowercase hex chars
    public string? ParentSpanId { get; init; }                // 16 lowercase hex chars; null for a root span
    public string? OperationName { get; init; }               // gen_ai.operation.name
    public string? AgentName { get; init; }                   // gen_ai.agent.name
    public string? ConversationId { get; init; }              // gen_ai.conversation.id
    public string? GitRepositorySlug { get; init; }           // "owner/repo" from github.copilot.git.repository (legacy copilot_chat.repo.remote_url)
    public string? GitBranch { get; init; }                   // github.copilot.git.branch (legacy copilot_chat.repo.head_branch_name)
    public string? GitCommitSha { get; init; }                // github.copilot.git.commit_sha (legacy copilot_chat.repo.head_commit_hash); lowercase 40 or 64 hex
    public required DateTimeOffset StartTime { get; init; }
}
