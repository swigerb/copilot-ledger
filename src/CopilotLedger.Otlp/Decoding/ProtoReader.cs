using System.Buffers.Binary;

namespace CopilotLedger.Otlp.Decoding;

internal ref struct ProtoReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public ProtoReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public bool IsAtEnd => _position == _buffer.Length;

    public ulong ReadVarint()
    {
        ulong value = 0;
        for (int index = 0; index < 10; index++)
        {
            if (_position == _buffer.Length)
            {
                throw new OtlpDecodeException("truncated varint");
            }

            byte current = _buffer[_position++];
            if (index == 9 && current > 0x01)
            {
                throw new OtlpDecodeException("invalid varint");
            }

            value |= (ulong)(current & 0x7f) << (index * 7);
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }

        throw new OtlpDecodeException("invalid varint");
    }

    public bool TryReadTag(out int fieldNumber, out int wireType)
    {
        if (IsAtEnd)
        {
            fieldNumber = 0;
            wireType = 0;
            return false;
        }

        ulong tag = ReadVarint();
        if (tag > uint.MaxValue)
        {
            throw new OtlpDecodeException("tag exceeds uint.MaxValue");
        }

        fieldNumber = (int)(tag >> 3);
        wireType = (int)(tag & 7);
        if (fieldNumber == 0)
        {
            throw new OtlpDecodeException("field number is zero");
        }

        return true;
    }

    public ulong ReadFixed64()
    {
        ReadOnlySpan<byte> value = ReadBytes(8, "truncated fixed64");
        return BinaryPrimitives.ReadUInt64LittleEndian(value);
    }

    public uint ReadFixed32()
    {
        ReadOnlySpan<byte> value = ReadBytes(4, "truncated fixed32");
        return BinaryPrimitives.ReadUInt32LittleEndian(value);
    }

    public ReadOnlySpan<byte> ReadLengthDelimited()
    {
        ulong length = ReadVarint();
        if (length > (ulong)(_buffer.Length - _position))
        {
            throw new OtlpDecodeException("length exceeds remaining bytes");
        }

        return ReadBytes((int)length, "truncated length-delimited field");
    }

    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case 0:
                _ = ReadVarint();
                break;
            case 1:
                _ = ReadFixed64();
                break;
            case 2:
                _ = ReadLengthDelimited();
                break;
            case 5:
                _ = ReadFixed32();
                break;
            default:
                throw new OtlpDecodeException($"unsupported wire type {wireType}");
        }
    }

    private ReadOnlySpan<byte> ReadBytes(int count, string failureMessage)
    {
        if (count > _buffer.Length - _position)
        {
            throw new OtlpDecodeException(failureMessage);
        }

        ReadOnlySpan<byte> result = _buffer.Slice(_position, count);
        _position += count;
        return result;
    }
}
