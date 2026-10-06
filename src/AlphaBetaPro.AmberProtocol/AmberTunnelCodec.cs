using System.Buffers.Binary;

namespace AlphaBetaPro.AmberProtocol;

public static class AmberTunnelCodec
{
    public const int RequestSize = 16;
    public const int StandardResponseSize = 140;
    public const int SpectrumEx2ResponseSize = 1044;
    public const int SpectrumBlockSize = 256;

    private static ReadOnlySpan<byte> Preamble => [0xA5, 0x5A];
    private static ReadOnlySpan<byte> RequestEnd => [0xB9, 0x9B];

    public static byte RouteFor(AmberTract tract) => tract switch
    {
        AmberTract.Tract1 => 0x90,
        AmberTract.Tract2 => 0x91,
        _ => throw new ArgumentOutOfRangeException(nameof(tract)),
    };

    public static byte[] BuildRequest(
        AmberTract tract,
        McaCommand command,
        ReadOnlySpan<byte> parameters = default)
    {
        if (parameters.Length > 6)
        {
            throw new ArgumentException("MCA-527 parameters must fit in six bytes.", nameof(parameters));
        }

        var frame = new byte[RequestSize];
        frame[0] = 0xC0;
        frame[1] = RouteFor(tract);
        Preamble.CopyTo(frame.AsSpan(2, 2));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)command);
        parameters.CopyTo(frame.AsSpan(6, parameters.Length));
        RequestEnd.CopyTo(frame.AsSpan(12, 2));
        frame[14] = Crc8Maxim.Compute(frame.AsSpan(1, 13));
        frame[15] = 0x0C;
        return frame;
    }

    public static AmberResponse ParseResponse(AmberTract tract, byte[] frame)
    {
        if (frame.Length is not StandardResponseSize and not SpectrumEx2ResponseSize)
        {
            throw new AmberProtocolException($"Unexpected AMBER response length: {frame.Length}.");
        }

        var expectedRoute = RouteFor(tract);
        if (frame[0] != 0xC0 || frame[1] != expectedRoute)
        {
            throw new AmberProtocolException(
                $"Unexpected tunnel header: {frame[0]:X2} {frame[1]:X2}; expected C0 {expectedRoute:X2}.");
        }

        if (!frame.AsSpan(2, 2).SequenceEqual(Preamble) || frame[^1] != 0x0C)
        {
            throw new AmberProtocolException("Invalid MCA-527 response framing.");
        }

        var expectedCrc = Crc8Maxim.Compute(frame.AsSpan(1, frame.Length - 3));
        if (frame[^2] != expectedCrc)
        {
            throw new AmberProtocolException(
                $"Invalid tunnel CRC: {frame[^2]:X2}; expected {expectedCrc:X2}.");
        }

        var flag = (McaEndFlag)BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(frame.Length - 4, 2));
        return new AmberResponse(tract, flag, frame);
    }

    public static uint[] ParseSpectrumEx2(AmberResponse response)
    {
        if (!response.IsSuccess)
        {
            throw new AmberProtocolException($"MCA-527 returned {response.EndFlag}.");
        }

        if (response.Frame.Length != SpectrumEx2ResponseSize)
        {
            throw new AmberProtocolException("The response is not a QUERY_SPECTRA_EX2 block.");
        }

        var result = new uint[SpectrumBlockSize];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                response.Frame.AsSpan(4 + index * sizeof(uint), sizeof(uint)));
        }

        return result;
    }

    public static McaStateSnapshot ParseState(AmberResponse response)
    {
        EnsureStandardSuccess(response);
        var frame = response.Frame;
        return new McaStateSnapshot(
            response.Tract,
            (McaRunState)BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(132, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(40, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(44, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(46, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(24, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(28, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(32, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(90, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(60, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(66, 2)));
    }

    public static ushort ParseState527ExRealTimeMilliseconds(AmberResponse response)
    {
        EnsureStandardSuccess(response);
        return BinaryPrimitives.ReadUInt16LittleEndian(response.Frame.AsSpan(86, 2));
    }

    public static McaSystemData ParseSystemData(AmberResponse response)
    {
        EnsureStandardSuccess(response);
        var frame = response.Frame;
        ulong detected = 0;
        for (var index = 0; index < 6; index++)
        {
            detected |= (ulong)frame[14 + index] << (8 * index);
        }

        return new McaSystemData(
            response.Tract,
            (McaRunState)BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(132, 2)),
            detected,
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(40, 4)));
    }

    private static void EnsureStandardSuccess(AmberResponse response)
    {
        if (!response.IsSuccess)
        {
            throw new AmberProtocolException($"MCA-527 returned {response.EndFlag}.");
        }

        if (response.Frame.Length != StandardResponseSize)
        {
            throw new AmberProtocolException("Expected a standard MCA-527 response.");
        }
    }
}
