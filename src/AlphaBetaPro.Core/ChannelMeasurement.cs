using AlphaBetaPro.AmberProtocol;

namespace AlphaBetaPro.Core;

public sealed record ChannelMeasurement(
    AmberTract Tract,
    McaRunState RunState,
    int ChannelCount,
    ushort SerialNumber,
    TimeSpan RealTime,
    TimeSpan LiveTime,
    TimeSpan DeadTime,
    uint CountRate,
    ulong TotalCounts,
    uint[] Spectrum);
