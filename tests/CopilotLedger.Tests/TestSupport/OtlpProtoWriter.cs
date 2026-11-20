using System.Buffers.Binary;
using System.Text;

namespace CopilotLedger.Tests.TestSupport;

public sealed class OtlpProtoWriter
{
    private readonly List<byte> _bytes = [];

    public OtlpProtoWriter Varint(int field, ulong value)
    {
        WriteVarint((ulong)((field << 3) | 0));
        WriteVarint(value);
        return this;
    }

    public OtlpProtoWriter Fixed64(int field, ulong value)
    {
        WriteVarint((ulong)((field << 3) | 1));
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        _bytes.AddRange(bytes.ToArray());
        return this;
    }

    public OtlpProtoWriter Fixed32(int field, uint value)
    {
        WriteVarint((ulong)((field << 3) | 5));
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        _bytes.AddRange(bytes.ToArray());
        return this;
    }

    public OtlpProtoWriter Bytes(int field, ReadOnlySpan<byte> value)
    {
        WriteVarint((ulong)((field << 3) | 2));
        WriteVarint((ulong)value.Length);
        _bytes.AddRange(value.ToArray());
        return this;
    }

    public OtlpProtoWriter String(int field, string value) =>
        Bytes(field, Encoding.UTF8.GetBytes(value));

    public OtlpProtoWriter Message(int field, OtlpProtoWriter message) =>
        Bytes(field, message.ToArray());

    public OtlpProtoWriter Raw(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    public byte[] ToArray() => [.. _bytes];

    private void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _bytes.Add((byte)((value & 0x7f) | 0x80));
            value >>= 7;
        }

        _bytes.Add((byte)value);
    }
}
