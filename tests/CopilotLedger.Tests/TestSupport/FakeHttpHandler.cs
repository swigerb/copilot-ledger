namespace CopilotLedger.Tests.TestSupport;

public sealed record HttpCall(string Method, string Uri, string UserAgent);

// Answers every request with respond(request). An exception that respond throws becomes the faulted result of SendAsync.
public sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpCall> Calls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls.Add(new HttpCall(
            request.Method.Method,
            request.RequestUri!.AbsoluteUri,
            request.Headers.UserAgent.ToString()));
        try
        {
            return Task.FromResult(respond(request));
        }
        catch (Exception ex)
        {
            return Task.FromException<HttpResponseMessage>(ex);
        }
    }

    // 200 with the bytes and no Content-Type, like any body the product reads as bytes.
    public static HttpResponseMessage Bytes(byte[] body) =>
        new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
}
