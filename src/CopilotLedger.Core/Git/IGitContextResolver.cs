namespace CopilotLedger.Core.Git;

public interface IGitContextResolver
{
    // Returns null when workingDirectory doesn't exist or isn't inside a Git working tree.
    GitWorkspaceContext? Resolve(string workingDirectory);
}
