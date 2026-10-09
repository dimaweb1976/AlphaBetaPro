using System.Windows;
using AlphaBetaPro.Core;
using AlphaBetaPro.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AlphaBetaPro.Wpf;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var collection = new ServiceCollection();
        collection.AddSingleton<IAmberDeviceService, AmberDeviceService>();
        collection.AddSingleton<MainViewModel>();
        collection.AddSingleton<MainWindow>();
        _services = collection.BuildServiceProvider();
        MainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
