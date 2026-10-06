namespace AlphaBetaPro.AmberProtocol;

public static class Crc8Maxim
{
    public static byte Compute(ReadOnlySpan<byte> data)
    {
        byte value = 0;
        foreach (var item in data)
        {
            value ^= item;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (byte)((value & 1) != 0
                    ? (value >> 1) ^ 0x8C
                    : value >> 1);
            }
        }

        return value;
    }
}
