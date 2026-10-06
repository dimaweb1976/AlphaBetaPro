using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using AlphaBetaPro.AmberProtocol;

var failures = new List<string>();
var passed = 0;
Run("captured CRC tract 1", TestCapturedTract1Crc);
Run("captured CRC tract 2", TestCapturedTract2Crc);
Run("QUERY_STATE request vector", TestQueryStateVector);
Run("QUERY_SPECTRA_EX2 decoding", TestSpectrumDecoding);
Run("QUERY_STATE typed decoding", TestStateDecoding);
await RunAsync("command timeout disconnects", TestTimeoutAsync);

var goldenRoot = ValueAfter("--golden-root");
if (goldenRoot is not null)
{
    Run("golden tract 1 integrity", () => TestGolden(goldenRoot, 1));
    Run("golden tract 2 integrity", () => TestGolden(goldenRoot, 2));
}

if (args.Contains("--network", StringComparer.OrdinalIgnoreCase))
{
    await RunNetworkAsync();
}

Console.WriteLine(failures.Count == 0
    ? $"PASS: {passed} tests"
    : $"FAIL: {failures.Count} test(s)");
foreach (var failure in failures)
{
    Console.Error.WriteLine(failure);
}

return failures.Count == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try
    {
        test();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failures.Add($"FAIL {name}: {error.Message}");
    }
}

async Task RunAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failures.Add($"FAIL {name}: {error.Message}");
    }
}

void TestCapturedTract1Crc()
{
    var frame = Convert.FromHexString("C090A55A5A00000000000000B99B6C0C");
    Equal(frame[14], Crc8Maxim.Compute(frame.AsSpan(1, 13)));
}

void TestCapturedTract2Crc()
{
    var frame = Convert.FromHexString("C091A55A1001000000000000B99BE50C");
    Equal(frame[14], Crc8Maxim.Compute(frame.AsSpan(1, 13)));
}

void TestQueryStateVector()
{
    var actual = AmberTunnelCodec.BuildRequest(AmberTract.Tract1, McaCommand.QueryState);
    var expected = Convert.FromHexString("C090A55A5A00000000000000B99B6C0C");
    if (!actual.SequenceEqual(expected))
    {
        throw new InvalidOperationException($"Expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}.");
    }
}

void TestSpectrumDecoding()
{
    var frame = BuildSpectrumResponse(AmberTract.Tract2);
    var response = AmberTunnelCodec.ParseResponse(AmberTract.Tract2, frame);
    var spectrum = AmberTunnelCodec.ParseSpectrumEx2(response);
    Equal(256, spectrum.Length);
    Equal(1000U, spectrum[0]);
    Equal(1255U, spectrum[255]);
}

void TestStateDecoding()
{
    var response = AmberTunnelCodec.ParseResponse(
        AmberTract.Tract1,
        BuildStateResponse(AmberTract.Tract1));
    var state = AmberTunnelCodec.ParseState(response);
    Equal(McaRunState.Running, state.RunState);
    Equal(1024, state.ChannelCount);
    Equal(15U, state.RealTimeSeconds);
    Equal(120U, state.CountRate);
    Equal((ushort)52701, state.SerialNumber);
    Equal(120U, state.DeadTimeMilliseconds);
    Equal(14.88, state.LiveTime.TotalSeconds);
}

void TestGolden(string root, int tract)
{
    var path = Path.Combine(root, $"tract{tract}", "golden-spectrum.json");
    var golden = GoldenSpectrum.Load(path);
    Equal(tract, (int)golden.Tract);
    Equal(golden.ChannelCount, golden.Counts.Length);
    var comparison = golden.Compare(golden.Counts);
    if (!comparison.Exact || comparison.DifferentChannels != 0)
    {
        throw new InvalidOperationException("Self-comparison of a valid golden spectrum failed.");
    }
}

async Task TestTimeoutAsync()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    try
    {
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var client = new AmberClient(TimeSpan.FromMilliseconds(150));
        var accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync("127.0.0.1", port);
        using var accepted = await accept;
        try
        {
            await client.QueryStateAsync(AmberTract.Tract1);
            throw new InvalidOperationException("Expected TimeoutException.");
        }
        catch (TimeoutException)
        {
        }

        if (client.IsConnected)
        {
            throw new InvalidOperationException("Timed-out client remained connected.");
        }
    }
    finally
    {
        listener.Stop();
    }
}

async Task RunNetworkAsync()
{
    var host = ValueAfter("--host") ?? "127.0.0.1";
    var port = int.Parse(ValueAfter("--port") ?? "50000");
    await using var client = new AmberClient();
    try
    {
        await client.ConnectAsync(host, port);
        foreach (var tract in new[] { AmberTract.Tract1, AmberTract.Tract2 })
        {
            var state = await client.GetStateAsync(tract);
            Equal(1024, state.ChannelCount);

            var spectrum = await client.ReadSpectrumAsync(tract, 1024);
            Equal(1024, spectrum.Length);
            passed++;
            Console.WriteLine(
                $"PASS network tract {(int)tract}: state={state.RunState}, " +
                $"real={state.RealTimeSeconds}s, total={spectrum.Aggregate(0UL, (s, v) => s + v)}");
        }

        await client.DisconnectAsync();
        if (client.IsConnected) throw new InvalidOperationException("DisconnectAsync did not reset state.");
        await client.ConnectAsync(host, port);
        var reconnected = await client.GetStateAsync(AmberTract.Tract1);
        Equal(1024, reconnected.ChannelCount);
        passed++;
        Console.WriteLine("PASS network reconnect");
    }
    catch (Exception error)
    {
        failures.Add($"FAIL network: {error.Message}");
    }
}

string? ValueAfter(string option)
{
    var index = Array.FindIndex(args, value => value.Equals(option, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static byte[] BuildSpectrumResponse(AmberTract tract)
{
    var frame = new byte[AmberTunnelCodec.SpectrumEx2ResponseSize];
    frame[0] = 0xC0;
    frame[1] = AmberTunnelCodec.RouteFor(tract);
    frame[2] = 0xA5;
    frame[3] = 0x5A;
    for (var index = 0; index < 256; index++)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4 + index * 4, 4), (uint)(1000 + index));
    }

    frame[^4] = 0xB9;
    frame[^3] = 0x9B;
    frame[^2] = Crc8Maxim.Compute(frame.AsSpan(1, frame.Length - 3));
    frame[^1] = 0x0C;
    return frame;
}

static byte[] BuildStateResponse(AmberTract tract)
{
    var frame = new byte[AmberTunnelCodec.StandardResponseSize];
    frame[0] = 0xC0;
    frame[1] = AmberTunnelCodec.RouteFor(tract);
    frame[2] = 0xA5;
    frame[3] = 0x5A;
    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24, 4), 15);
    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(28, 4), 120);
    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(32, 4), 120);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(40, 2), 1024);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(44, 2), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(46, 2), 4095);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(60, 2), 70);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(66, 2), 3);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(90, 2), 52701);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(132, 2), (ushort)McaRunState.Running);
    frame[^4] = 0xB9;
    frame[^3] = 0x9B;
    frame[^2] = Crc8Maxim.Compute(frame.AsSpan(1, frame.Length - 3));
    frame[^1] = 0x0C;
    return frame;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
