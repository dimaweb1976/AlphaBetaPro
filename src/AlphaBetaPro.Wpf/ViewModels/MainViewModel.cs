using System.Collections.ObjectModel;
using AlphaBetaPro.AmberProtocol;
using AlphaBetaPro.Core;
using AlphaBetaPro.Wpf.Infrastructure;

namespace AlphaBetaPro.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IAmberDeviceService _device;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(1);
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollTask;
    private string _host = "127.0.0.1";
    private string _port = "50000";
    private bool _isConnected;
    private bool _isBusy;
    private string _statusText = "Отключено";
    private ChannelViewModel? _selectedChannel;

    public MainViewModel(IAmberDeviceService device)
    {
        _device = device;
        Channels = new ObservableCollection<ChannelViewModel>
        {
            new(AmberTract.Tract1),
            new(AmberTract.Tract2),
        };
        _selectedChannel = Channels[0];
        ConnectCommand = new AsyncCommand(ConnectAsync, () => !IsConnected && !IsBusy);
        DisconnectCommand = new AsyncCommand(DisconnectAsync, () => IsConnected && !IsBusy);
        ClearCommand = new AsyncCommand(() => ExecuteForSelectedAsync(_device.ClearAsync), CanClearOrStart);
        StartCommand = new AsyncCommand(() => ExecuteForSelectedAsync(_device.StartAsync), CanClearOrStart);
        StopCommand = new AsyncCommand(() => ExecuteForSelectedAsync(_device.StopAsync), CanStop);
    }

    public ObservableCollection<ChannelViewModel> Channels { get; }
    public ObservableCollection<string> Log { get; } = [];
    public AsyncCommand ConnectCommand { get; }
    public AsyncCommand DisconnectCommand { get; }
    public AsyncCommand ClearCommand { get; }
    public AsyncCommand StartCommand { get; }
    public AsyncCommand StopCommand { get; }
    public string Host { get => _host; set => SetProperty(ref _host, value); }
    public string Port { get => _port; set => SetProperty(ref _port, value); }
    public bool IsConnected { get => _isConnected; private set { if (SetProperty(ref _isConnected, value)) RefreshCommands(); } }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RefreshCommands(); } }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public ChannelViewModel? SelectedChannel
    {
        get => _selectedChannel;
        set { if (SetProperty(ref _selectedChannel, value)) RefreshCommands(); }
    }

    private async Task ConnectAsync()
    {
        if (!int.TryParse(Port, out var port) || port is < 1 or > 65535)
        {
            StatusText = "Некорректный порт";
            return;
        }

        IsBusy = true;
        try
        {
            await _device.ConnectAsync(Host.Trim(), port);
            IsConnected = true;
            StatusText = $"Подключено: {Host.Trim()}:{port}";
            AddLog(StatusText);
            await RefreshAllAsync(CancellationToken.None);
            _pollCancellation = new CancellationTokenSource();
            _pollTask = PollAsync(_pollCancellation.Token);
        }
        catch (Exception error)
        {
            StatusText = $"Ошибка подключения: {error.Message}";
            AddLog(StatusText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisconnectAsync()
    {
        IsBusy = true;
        try
        {
            _pollCancellation?.Cancel();
            if (_pollTask is not null)
            {
                try { await _pollTask; } catch (OperationCanceledException) { }
            }
            await _device.DisconnectAsync();
            IsConnected = false;
            StatusText = "Отключено";
            foreach (var channel in Channels) channel.MarkDisconnected();
            AddLog(StatusText);
        }
        finally
        {
            _pollCancellation?.Dispose();
            _pollCancellation = null;
            _pollTask = null;
            IsBusy = false;
        }
    }

    private async Task ExecuteForSelectedAsync(
        Func<AmberTract, CancellationToken, Task> operation)
    {
        var channel = SelectedChannel;
        if (channel is null) return;
        IsBusy = true;
        try
        {
            await operation(channel.Tract, CancellationToken.None);
            AddLog($"{channel.Name}: команда выполнена");
            await RefreshChannelAsync(channel, CancellationToken.None);
        }
        catch (Exception error)
        {
            StatusText = $"Ошибка {channel.Name}: {error.Message}";
            AddLog(StatusText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, cancellationToken);
                await RefreshAllAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                StatusText = $"Ошибка опроса: {error.Message}";
                AddLog(StatusText);
                break;
            }
        }
    }

    private async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        foreach (var channel in Channels)
            await RefreshChannelAsync(channel, cancellationToken);
        RefreshCommands();
    }

    private async Task RefreshChannelAsync(ChannelViewModel channel, CancellationToken cancellationToken) =>
        channel.Apply(await _device.ReadChannelAsync(channel.Tract, cancellationToken));

    private bool CanClearOrStart() =>
        IsConnected && !IsBusy && SelectedChannel is { IsRunning: false };

    private bool CanStop() =>
        IsConnected && !IsBusy && SelectedChannel is { IsRunning: true };

    private void RefreshCommands()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }

    private void AddLog(string message)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (Log.Count > 100) Log.RemoveAt(Log.Count - 1);
    }

    public async Task ShutdownAsync()
    {
        if (IsConnected) await DisconnectAsync();
        await _device.DisposeAsync();
    }
}
