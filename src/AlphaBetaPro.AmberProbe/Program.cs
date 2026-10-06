using System.Text.Json;
using AlphaBetaPro.AmberProtocol;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

try
{
    var options = ParseOptions(args.Skip(1).ToArray());
    var host = options.GetValueOrDefault("host", "127.0.0.1");
    var port = int.Parse(options.GetValueOrDefault("port", "50000"));
    var tract = options.GetValueOrDefault("tract", "1") == "2"
        ? AmberTract.Tract2
        : AmberTract.Tract1;
    var channels = int.Parse(options.GetValueOrDefault("channels", "1024"));
    var timeout = TimeSpan.FromSeconds(double.Parse(options.GetValueOrDefault("timeout", "60")));

    using var cancellation = new CancellationTokenSource(timeout);
    await using var client = new AmberClient();
    await client.ConnectAsync(host, port, cancellationToken: cancellation.Token);

    if (args[0].Equals("state", StringComparison.OrdinalIgnoreCase))
    {
        var state = await client.GetStateAsync(tract, cancellation.Token);
        var system = await client.GetSystemDataAsync(tract, cancellation.Token);
        await WriteResultAsync(options.GetValueOrDefault("output"), new
        {
            tract = (int)tract,
            run_state = state.RunState.ToString(),
            channels = state.ChannelCount,
            lld = state.Lld,
            uld = state.Uld,
            real_time_s = state.RealTime.TotalSeconds,
            live_time_s = state.LiveTime.TotalSeconds,
            dead_time_ms = state.DeadTimeMilliseconds,
            count_rate = state.CountRate,
            detected_counts = system.DetectedCounts,
            serial_number = state.SerialNumber,
            bias_setting = state.BiasSetting,
            pzc_value = state.PzcValue,
        });
        return 0;
    }

    if (args[0].Equals("compare", StringComparison.OrdinalIgnoreCase))
    {
        if (!options.TryGetValue("golden", out var goldenPath))
        {
            throw new ArgumentException("compare requires --golden <path>.");
        }

        var golden = GoldenSpectrum.Load(goldenPath);
        if (golden.Tract != tract)
        {
            throw new ArgumentException(
                $"Golden belongs to tract {(int)golden.Tract}, requested tract is {(int)tract}.");
        }

        var spectrum = await client.ReadSpectrumAsync(tract, golden.ChannelCount, cancellation.Token);
        var comparison = golden.Compare(spectrum);
        await WriteResultAsync(options.GetValueOrDefault("output"), new
        {
            result = comparison.Exact ? "PASS" : "FAIL",
            tract = (int)tract,
            channels = golden.ChannelCount,
            different_channels = comparison.DifferentChannels,
            first_different_channel = comparison.FirstDifferentChannel,
            expected_total = comparison.ExpectedTotal,
            actual_total = comparison.ActualTotal,
            expected_sha256 = golden.SpectrumSha256,
            actual_sha256 = comparison.ActualSha256,
        });
        return comparison.Exact ? 0 : 2;
    }

    if (args[0].Equals("read", StringComparison.OrdinalIgnoreCase))
    {
        var spectrum = await client.ReadSpectrumAsync(tract, channels, cancellation.Token);
        await WriteResultAsync(options.GetValueOrDefault("output"), new
        {
            host,
            port,
            tract = (int)tract,
            channels = spectrum.Length,
            total_counts = spectrum.Aggregate(0UL, (sum, value) => sum + value),
            spectrum,
        });
        return 0;
    }

    if (args[0].Equals("smoke", StringComparison.OrdinalIgnoreCase))
    {
        var duration = TimeSpan.FromSeconds(double.Parse(options.GetValueOrDefault("duration", "2")));
        EnsureSuccess(await client.ClearAsync(tract, cancellation.Token), "CLEAR");
        EnsureSuccess(await client.StartAsync(tract, cancellationToken: cancellation.Token), "START");
        await Task.Delay(duration, cancellation.Token);
        EnsureSuccess(await client.StopAsync(tract, cancellation.Token), "STOP");
        var spectrum = await client.ReadSpectrumAsync(tract, channels, cancellation.Token);
        var total = spectrum.Aggregate(0UL, (sum, value) => sum + value);
        await WriteResultAsync(options.GetValueOrDefault("output"), new
        {
            result = total > 0 ? "PASS" : "FAIL",
            host,
            port,
            tract = (int)tract,
            duration_s = duration.TotalSeconds,
            channels = spectrum.Length,
            total_counts = total,
            nonzero_channels = spectrum.Count(value => value != 0),
            spectrum,
        });
        return total > 0 ? 0 : 2;
    }

    Console.Error.WriteLine($"Unknown command: {args[0]}");
    PrintHelp();
    return 1;
}
catch (Exception error)
{
    Console.Error.WriteLine($"ERROR: {error.GetType().Name}: {error.Message}");
    return 1;
}

static void EnsureSuccess(AmberResponse response, string operation)
{
    if (!response.IsSuccess)
    {
        throw new AmberProtocolException($"{operation} failed: {response.EndFlag}.");
    }
}

static Dictionary<string, string> ParseOptions(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < values.Length; index += 2)
    {
        if (!values[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= values.Length)
        {
            throw new ArgumentException($"Invalid option near '{values[index]}'.");
        }

        result[values[index][2..]] = values[index + 1];
    }

    return result;
}

static async Task WriteResultAsync(string? output, object value)
{
    var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    if (string.IsNullOrWhiteSpace(output))
    {
        Console.WriteLine(json);
        return;
    }

    await File.WriteAllTextAsync(output, json);
    Console.WriteLine($"Saved: {Path.GetFullPath(output)}");
}

static void PrintHelp()
{
    Console.WriteLine("""
        AlphaBetaPro AMBER diagnostic client

        Commands:
          state --host 127.0.0.1 --port 50000 --tract 1 [--output state.json]
          read  --host 127.0.0.1 --port 50000 --tract 1 --channels 1024 [--output spectrum.json]
          smoke --host 127.0.0.1 --port 50000 --tract 1 --channels 1024 --duration 2 [--output run.json]
          compare --host 127.0.0.1 --port 50000 --tract 1 --golden golden-spectrum.json [--output compare.json]

        smoke performs CLEAR -> START -> wait -> STOP -> spectrum read.
        """);
}
