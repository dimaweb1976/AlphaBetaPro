using AlphaBetaPro.AmberProtocol;
using AlphaBetaPro.Core;
using AlphaBetaPro.Wpf.Infrastructure;

namespace AlphaBetaPro.Wpf.ViewModels;

public sealed class ChannelViewModel : ObservableObject
{
    private string _runState = "Disconnected";
    private string _serialNumber = "—";
    private int _channelCount;
    private string _realTime = "0.000 s";
    private string _liveTime = "0.000 s";
    private string _deadTime = "0 ms";
    private uint _countRate;
    private ulong _totalCounts;
    private string _spectrumPreview = "Нет данных";

    public ChannelViewModel(AmberTract tract)
    {
        Tract = tract;
        Name = $"Канал {(int)tract}";
    }

    public AmberTract Tract { get; }
    public string Name { get; }
    public string RunState { get => _runState; private set => SetProperty(ref _runState, value); }
    public string SerialNumber { get => _serialNumber; private set => SetProperty(ref _serialNumber, value); }
    public int ChannelCount { get => _channelCount; private set => SetProperty(ref _channelCount, value); }
    public string RealTime { get => _realTime; private set => SetProperty(ref _realTime, value); }
    public string LiveTime { get => _liveTime; private set => SetProperty(ref _liveTime, value); }
    public string DeadTime { get => _deadTime; private set => SetProperty(ref _deadTime, value); }
    public uint CountRate { get => _countRate; private set => SetProperty(ref _countRate, value); }
    public ulong TotalCounts { get => _totalCounts; private set => SetProperty(ref _totalCounts, value); }
    public string SpectrumPreview { get => _spectrumPreview; private set => SetProperty(ref _spectrumPreview, value); }
    public bool IsRunning => RunState == McaRunState.Running.ToString();

    public void Apply(ChannelMeasurement measurement)
    {
        RunState = measurement.RunState.ToString();
        SerialNumber = measurement.SerialNumber.ToString();
        ChannelCount = measurement.ChannelCount;
        RealTime = $"{measurement.RealTime.TotalSeconds:F3} s";
        LiveTime = $"{measurement.LiveTime.TotalSeconds:F3} s";
        DeadTime = $"{measurement.DeadTime.TotalMilliseconds:F0} ms";
        CountRate = measurement.CountRate;
        TotalCounts = measurement.TotalCounts;
        SpectrumPreview = string.Join(
            Environment.NewLine,
            measurement.Spectrum.Take(32).Select((value, index) => $"{index,4}: {value}"));
        RaisePropertyChanged(nameof(IsRunning));
    }

    public void MarkDisconnected()
    {
        RunState = "Disconnected";
        CountRate = 0;
        RaisePropertyChanged(nameof(IsRunning));
    }
}
