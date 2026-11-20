namespace CopilotLedger.Otlp.Decoding;

public sealed class OtlpDecodeException : Exception
{
    public OtlpDecodeException(string message) : base(message) { }
    public OtlpDecodeException(string message, Exception innerException) : base(message, innerException) { }
}
