using CopilotLedger.Core.Telemetry;

namespace CopilotLedger.Tests.Telemetry;

public sealed class GenAiOperationNamesTests
{
    [Theory]
    [InlineData("chat")]
    [InlineData("CHAT")]
    [InlineData("text_completion")]
    [InlineData("generate_content")]
    public void IsInference_InferenceOperation_ReturnsTrue(string operationName)
    {
        Assert.True(GenAiOperationNames.IsInference(operationName));
    }

    [Theory]
    [InlineData("invoke_agent")]
    [InlineData("execute_tool")]
    [InlineData("embeddings")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void IsInference_NonInferenceOperation_ReturnsFalse(string? operationName)
    {
        Assert.False(GenAiOperationNames.IsInference(operationName));
    }
}
