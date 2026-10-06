using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace AlphaBetaPro.AmberProtocol;

public sealed record GoldenSpectrum(
    string Format,
    AmberTract Tract,
    int ChannelCount,
    ulong TotalCounts,
    uint[] Counts,
    string SpectrumSha256)
{
    public static GoldenSpectrum Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var format = root.GetProperty("format").GetString() ?? string.Empty;
        if (format != "AlphaProGoldenSpectrum-1")
        {
            throw new InvalidDataException($"Unsupported golden format: {format}.");
        }

        var counts = root.GetProperty("counts").EnumerateArray()
            .Select(value => value.GetUInt32()).ToArray();
        var result = new GoldenSpectrum(
            format,
            (AmberTract)root.GetProperty("tract").GetInt32(),
            root.GetProperty("channel_count").GetInt32(),
            root.GetProperty("total_counts").GetUInt64(),
            counts,
            root.GetProperty("hashes").GetProperty("spectrum_uint32le_sha256").GetString() ?? string.Empty);
        result.Validate();
        return result;
    }

    public GoldenComparison Compare(IReadOnlyList<uint> actual)
    {
        var compared = Math.Min(Counts.Length, actual.Count);
        var differences = 0;
        int? firstDifference = null;
        for (var index = 0; index < compared; index++)
        {
            if (Counts[index] == actual[index]) continue;
            differences++;
            firstDifference ??= index;
        }

        differences += Math.Abs(Counts.Length - actual.Count);
        return new GoldenComparison(
            differences == 0,
            differences,
            firstDifference,
            TotalCounts,
            actual.Aggregate(0UL, (sum, value) => sum + value),
            ComputeSha256(actual));
    }

    public void Validate()
    {
        if (ChannelCount != Counts.Length)
            throw new InvalidDataException("Golden channel_count does not match counts length.");
        if (Counts.Aggregate(0UL, (sum, value) => sum + value) != TotalCounts)
            throw new InvalidDataException("Golden total_counts does not match counts.");
        if (!ComputeSha256(Counts).Equals(SpectrumSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Golden spectrum SHA-256 is invalid.");
    }

    public static string ComputeSha256(IReadOnlyList<uint> counts)
    {
        var bytes = new byte[counts.Count * sizeof(uint)];
        for (var index = 0; index < counts.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint), sizeof(uint)), counts[index]);
        }

        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}

public sealed record GoldenComparison(
    bool Exact,
    int DifferentChannels,
    int? FirstDifferentChannel,
    ulong ExpectedTotal,
    ulong ActualTotal,
    string ActualSha256);
