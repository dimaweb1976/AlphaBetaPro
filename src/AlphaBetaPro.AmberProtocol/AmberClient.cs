using System.Buffers.Binary;
using System.Net.Sockets;

namespace AlphaBetaPro.AmberProtocol;

public sealed class AmberClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _exchangeLock = new(1, 1);
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private bool _disposed;

    public AmberClient(TimeSpan? operationTimeout = null)
    {
        OperationTimeout = operationTimeout ?? TimeSpan.FromSeconds(5);
        if (OperationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        }
    }

    public TimeSpan OperationTimeout { get; }
    public bool IsConnected => _stream is not null;

    public async Task ConnectAsync(
        string host,
        int port = 50000,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _exchangeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ResetConnectionAsync().ConfigureAwait(false);
            using var timeout = CreateTimeout(cancellationToken);
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
                _tcpClient = client;
                _stream = client.GetStream();
            }
            catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
            {
                client.Dispose();
                throw new TimeoutException($"Timed out connecting to {host}:{port}.", error);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        finally
        {
            _exchangeLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _exchangeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ResetConnectionAsync().ConfigureAwait(false);
        }
        finally
        {
            _exchangeLock.Release();
        }
    }

    public Task<AmberResponse> ClearAsync(
        AmberTract tract,
        CancellationToken cancellationToken = default) =>
        ExchangeAsync(tract, McaCommand.Clear, cancellationToken: cancellationToken);

    public Task<AmberResponse> StopAsync(
        AmberTract tract,
        CancellationToken cancellationToken = default) =>
        ExchangeAsync(tract, McaCommand.Stop, cancellationToken: cancellationToken);

    public Task<AmberResponse> QueryStateAsync(
        AmberTract tract,
        CancellationToken cancellationToken = default) =>
        ExchangeAsync(tract, McaCommand.QueryState, cancellationToken: cancellationToken);

    public async Task<McaStateSnapshot> GetStateAsync(
        AmberTract tract,
        CancellationToken cancellationToken = default)
    {
        var state = AmberTunnelCodec.ParseState(
            await QueryStateAsync(tract, cancellationToken).ConfigureAwait(false));
        var extended = await ExchangeAsync(
            tract,
            McaCommand.QueryState527Ex,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return state with
        {
            RealTimeFractionMilliseconds =
                AmberTunnelCodec.ParseState527ExRealTimeMilliseconds(extended),
        };
    }

    public async Task<McaSystemData> GetSystemDataAsync(
        AmberTract tract,
        CancellationToken cancellationToken = default) =>
        AmberTunnelCodec.ParseSystemData(await ExchangeAsync(
            tract,
            McaCommand.QuerySystemData,
            cancellationToken: cancellationToken).ConfigureAwait(false));

    public Task<AmberResponse> StartAsync(
        AmberTract tract,
        ushort mode = 1,
        CancellationToken cancellationToken = default)
    {
        var parameters = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(parameters, mode);
        return ExchangeAsync(tract, McaCommand.Start, parameters, cancellationToken);
    }

    public async Task<uint[]> ReadSpectrumBlockAsync(
        AmberTract tract,
        ushort firstChannel,
        CancellationToken cancellationToken = default)
    {
        var parameters = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(parameters, firstChannel);
        BinaryPrimitives.WriteUInt16LittleEndian(parameters.AsSpan(2), 1);
        var response = await ExchangeAsync(
            tract,
            McaCommand.QuerySpectraEx2,
            parameters,
            cancellationToken).ConfigureAwait(false);
        return AmberTunnelCodec.ParseSpectrumEx2(response);
    }

    public async Task<uint[]> ReadSpectrumAsync(
        AmberTract tract,
        int channelCount,
        CancellationToken cancellationToken = default)
    {
        if (channelCount is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        }

        var spectrum = new uint[channelCount];
        for (var offset = 0; offset < channelCount; offset += AmberTunnelCodec.SpectrumBlockSize)
        {
            var block = await ReadSpectrumBlockAsync(
                tract,
                checked((ushort)offset),
                cancellationToken).ConfigureAwait(false);
            Array.Copy(block, 0, spectrum, offset, Math.Min(block.Length, channelCount - offset));
        }

        return spectrum;
    }

    public async Task<AmberResponse> ExchangeAsync(
        AmberTract tract,
        McaCommand command,
        byte[]? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _exchangeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stream = _stream ?? throw new InvalidOperationException("ConnectAsync must be called first.");
            using var timeout = CreateTimeout(cancellationToken);
            var request = AmberTunnelCodec.BuildRequest(tract, command, parameters ?? []);
            var responseSize = command == McaCommand.QuerySpectraEx2
                ? AmberTunnelCodec.SpectrumEx2ResponseSize
                : AmberTunnelCodec.StandardResponseSize;
            try
            {
                await stream.WriteAsync(request, timeout.Token).ConfigureAwait(false);
                var response = new byte[responseSize];
                await stream.ReadExactlyAsync(response, timeout.Token).ConfigureAwait(false);
                return AmberTunnelCodec.ParseResponse(tract, response);
            }
            catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
            {
                await ResetConnectionAsync().ConfigureAwait(false);
                throw new TimeoutException($"AMBER command {command} timed out.", error);
            }
            catch (Exception error) when (error is IOException or SocketException or EndOfStreamException)
            {
                await ResetConnectionAsync().ConfigureAwait(false);
                throw new AmberConnectionException($"AMBER connection failed during {command}.", error);
            }
        }
        finally
        {
            _exchangeLock.Release();
        }
    }

    private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(OperationTimeout);
        return source;
    }

    private async ValueTask ResetConnectionAsync()
    {
        var stream = _stream;
        _stream = null;
        if (stream is not null)
        {
            try
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
        }

        _tcpClient?.Dispose();
        _tcpClient = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await ResetConnectionAsync().ConfigureAwait(false);
        _exchangeLock.Dispose();
    }
}
