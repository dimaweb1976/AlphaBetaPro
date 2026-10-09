using AlphaBetaPro.AmberProtocol;

namespace AlphaBetaPro.Core;

public interface IAmberDeviceService : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<ChannelMeasurement> ReadChannelAsync(
        AmberTract tract,
        CancellationToken cancellationToken = default);
    Task ClearAsync(AmberTract tract, CancellationToken cancellationToken = default);
    Task StartAsync(AmberTract tract, CancellationToken cancellationToken = default);
    Task StopAsync(AmberTract tract, CancellationToken cancellationToken = default);
}
