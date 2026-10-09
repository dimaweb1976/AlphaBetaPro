using AlphaBetaPro.AmberProtocol;

namespace AlphaBetaPro.Core;

public sealed class AmberDeviceService : IAmberDeviceService, IDisposable
{
    private readonly AmberClient _client;

    public AmberDeviceService() : this(new AmberClient())
    {
    }

    internal AmberDeviceService(AmberClient client)
    {
        _client = client;
    }

    public bool IsConnected => _client.IsConnected;

    public Task ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default) =>
        _client.ConnectAsync(host, port, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        _client.DisconnectAsync(cancellationToken);

    public async Task<ChannelMeasurement> ReadChannelAsync(
        AmberTract tract,
        CancellationToken cancellationToken = default)
    {
        var state = await _client.GetStateAsync(tract, cancellationToken).ConfigureAwait(false);
        var system = await _client.GetSystemDataAsync(tract, cancellationToken).ConfigureAwait(false);
        var spectrum = await _client.ReadSpectrumAsync(
            tract,
            state.ChannelCount,
            cancellationToken).ConfigureAwait(false);
        return new ChannelMeasurement(
            tract,
            state.RunState,
            state.ChannelCount,
            state.SerialNumber,
            state.RealTime,
            state.LiveTime,
            TimeSpan.FromMilliseconds(state.DeadTimeMilliseconds),
            state.CountRate,
            system.DetectedCounts,
            spectrum);
    }

    public async Task ClearAsync(
        AmberTract tract,
        CancellationToken cancellationToken = default) =>
        EnsureSuccess(await _client.ClearAsync(tract, cancellationToken).ConfigureAwait(false), "CLEAR");

    public async Task StartAsync(
        AmberTract tract,
        CancellationToken cancellationToken = default) =>
        EnsureSuccess(await _client.StartAsync(tract, cancellationToken: cancellationToken).ConfigureAwait(false), "START");

    public async Task StopAsync(
        AmberTract tract,
        CancellationToken cancellationToken = default) =>
        EnsureSuccess(await _client.StopAsync(tract, cancellationToken).ConfigureAwait(false), "STOP");

    private static void EnsureSuccess(AmberResponse response, string operation)
    {
        if (!response.IsSuccess)
        {
            throw new AmberProtocolException($"{operation} failed: {response.EndFlag}.");
        }
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    public void Dispose() => _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
