using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace CopilotLedger.Otlp.Receiver;

internal enum OtlpEncoding
{
    Json,
    Protobuf,
}

internal static class OtlpHttp
{
    private const string ProtobufContentType = "application/x-protobuf";
    private const string JsonContentType = "application/json";

    public static OtlpEncoding EncodingFromContentType(string? contentType, out string mediaType)
    {
        mediaType = (contentType ?? string.Empty).Split(';', 2)[0].Trim();
        if (mediaType.Equals(ProtobufContentType, StringComparison.OrdinalIgnoreCase))
        {
            return OtlpEncoding.Protobuf;
        }

        return OtlpEncoding.Json;
    }

    public static bool IsSupportedContentType(string mediaType) =>
        mediaType.Equals(ProtobufContentType, StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals(JsonContentType, StringComparison.OrdinalIgnoreCase);

    public static async Task WriteEmptySuccessAsync(HttpResponse response, OtlpEncoding encoding)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = encoding == OtlpEncoding.Protobuf
            ? ProtobufContentType
            : JsonContentType;
        await response.Body.WriteAsync(
            encoding == OtlpEncoding.Protobuf ? [] : "{}"u8.ToArray(),
            response.HttpContext.RequestAborted);
    }

    public static async Task WritePartialSuccessAsync(
        HttpResponse response,
        OtlpEncoding encoding,
        int rejectedSpans,
        string? reason)
    {
        string message = $"{rejectedSpans} span(s) rejected: {reason}";
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = encoding == OtlpEncoding.Protobuf
            ? ProtobufContentType
            : JsonContentType;

        if (encoding == OtlpEncoding.Protobuf)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            byte[] inner = [0x08, .. EncodeVarint((ulong)rejectedSpans), 0x12,
                .. EncodeVarint((ulong)messageBytes.Length), .. messageBytes];
            await response.Body.WriteAsync(
                (byte[])[0x0A, .. EncodeVarint((ulong)inner.Length), .. inner],
                response.HttpContext.RequestAborted);
            return;
        }

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("partialSuccess");
            writer.WriteStartObject();
            writer.WriteString(
                "rejectedSpans",
                rejectedSpans.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("errorMessage", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        await response.Body.WriteAsync(buffer.WrittenMemory, response.HttpContext.RequestAborted);
    }

    public static async Task WriteErrorAsync(
        HttpResponse response,
        OtlpEncoding encoding,
        int statusCode,
        string message)
    {
        response.StatusCode = statusCode;
        response.ContentType = encoding == OtlpEncoding.Protobuf
            ? ProtobufContentType
            : JsonContentType;
        if (encoding == OtlpEncoding.Protobuf)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            await response.Body.WriteAsync(
                (byte[])[0x12, .. EncodeVarint((ulong)messageBytes.Length), .. messageBytes],
                response.HttpContext.RequestAborted);
            return;
        }

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("message", message);
            writer.WriteEndObject();
        }

        await response.Body.WriteAsync(buffer.WrittenMemory, response.HttpContext.RequestAborted);
    }

    private static byte[] EncodeVarint(ulong value)
    {
        using MemoryStream stream = new();
        do
        {
            byte current = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                current |= 0x80;
            }

            stream.WriteByte(current);
        } while (value != 0);

        return stream.ToArray();
    }
}
