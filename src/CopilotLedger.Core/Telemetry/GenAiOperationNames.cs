namespace CopilotLedger.Core.Telemetry;

public static class GenAiOperationNames
{
    public const string Chat = "chat";
    public const string TextCompletion = "text_completion";
    public const string GenerateContent = "generate_content";
    public const string InvokeAgent = "invoke_agent";
    public const string ExecuteTool = "execute_tool";

    // True only for Chat, TextCompletion, and GenerateContent (ordinal, case-insensitive). Null or blank returns false.
    public static bool IsInference(string? operationName) =>
        string.Equals(operationName, Chat, StringComparison.OrdinalIgnoreCase)
        || string.Equals(operationName, TextCompletion, StringComparison.OrdinalIgnoreCase)
        || string.Equals(operationName, GenerateContent, StringComparison.OrdinalIgnoreCase);
}
