namespace CopilotLedger.Otlp.Receiver;

public sealed record OtlpReceiverOptions
{
    public const int DefaultPort = 4318;
    public const long DefaultMaxRequestBodyBytes = 64L * 1024 * 1024;
    public int Port { get; init; } = DefaultPort;
    public long MaxRequestBodyBytes { get; init; } = DefaultMaxRequestBodyBytes;   // applies to raw and decompressed bodies
}
