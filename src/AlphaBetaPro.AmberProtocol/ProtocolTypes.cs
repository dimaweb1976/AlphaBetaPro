namespace AlphaBetaPro.AmberProtocol;

public enum AmberTract
{
    Tract1 = 1,
    Tract2 = 2,
}

public enum McaCommand : ushort
{
    Init = 0x0041,
    Start = 0x0042,
    Stop = 0x0043,
    Clear = 0x0044,
    SetAdcResolution = 0x0046,
    SetPresets = 0x0048,
    SetGain = 0x004C,
    SetBias = 0x004F,
    SetMeasurePzc = 0x0058,
    QueryPower = 0x0059,
    QueryState = 0x005A,
    QuerySpectra = 0x005B,
    QuerySystemData = 0x0062,
    QueryState527 = 0x0101,
    QuerySpectraEx = 0x0102,
    QueryState527Ex = 0x0110,
    QueryState527Ex2 = 0x012F,
    QuerySpectraEx2 = 0x0138,
    QueryState527Ex3 = 0x013F,
}

public enum McaEndFlag : ushort
{
    Ok = 0x9BB9,
    Timeout = 0xAAA4,
    InvalidFrame = 0xAAA6,
    InvalidParameter = 0xAAAA,
    UnknownCommand = 0xAAAB,
    MeasurementRunning = 0xAAAC,
}

public sealed record AmberResponse(
    AmberTract Tract,
    McaEndFlag EndFlag,
    byte[] Frame)
{
    public bool IsSuccess => EndFlag == McaEndFlag.Ok;
}

public enum McaRunState : ushort
{
    Ready = 1,
    Running = 2,
}

public sealed record McaStateSnapshot(
    AmberTract Tract,
    McaRunState RunState,
    int ChannelCount,
    int Lld,
    int Uld,
    uint RealTimeSeconds,
    uint CountRate,
    uint DeadTimeMilliseconds,
    ushort SerialNumber,
    ushort BiasSetting,
    ushort PzcValue,
    ushort RealTimeFractionMilliseconds = 0)
{
    public TimeSpan RealTime =>
        TimeSpan.FromMilliseconds(RealTimeSeconds * 1000UL + RealTimeFractionMilliseconds);

    public TimeSpan LiveTime =>
        TimeSpan.FromMilliseconds(Math.Max(0, RealTime.TotalMilliseconds - DeadTimeMilliseconds));
}

public sealed record McaSystemData(
    AmberTract Tract,
    McaRunState RunState,
    ulong DetectedCounts,
    uint RealTimeSeconds);
