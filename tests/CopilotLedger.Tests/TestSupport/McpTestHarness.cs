using System.IO.Pipelines;
using CopilotLedger.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CopilotLedger.Tests.TestSupport;

public sealed class McpTestHarness : IAsyncDisposable
{
    private readonly Pipe _toServer;
    private readonly Pipe _toClient;
    private readonly Task _server;

    private McpTestHarness(
        McpClient client,
        Pipe toServer,
        Pipe toClient,
        Task server)
    {
        Client = client;
        _toServer = toServer;
        _toClient = toClient;
        _server = server;
    }

    public McpClient Client { get; }

    // Starts LedgerMcpServer.RunAsync(context, toServer.Reader.AsStream(), toClient.Writer.AsStream()) and connects
    // McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()),
    //     new McpClientOptions { ClientInfo = new Implementation { Name = "ledger-tests", Version = "1.0" } }).
    public static async Task<McpTestHarness> StartAsync(LedgerMcpContext context)
    {
        Pipe toServer = new();
        Pipe toClient = new();
        Task server = LedgerMcpServer.RunAsync(
            context,
            toServer.Reader.AsStream(),
            toClient.Writer.AsStream());
        McpClient client = await McpClient.CreateAsync(
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
        return new McpTestHarness(client, toServer, toClient, server);
    }

    // Disposes the client, completes toServer.Writer, and waits up to 10 seconds for the server task.
    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _toServer.Writer.CompleteAsync();
        await _server.WaitAsync(TimeSpan.FromSeconds(10));
        await _toClient.Reader.CompleteAsync();
    }
}
