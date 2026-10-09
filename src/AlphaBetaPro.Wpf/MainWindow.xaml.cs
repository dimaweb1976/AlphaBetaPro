using System.ComponentModel;
using System.Windows;
using AlphaBetaPro.Wpf.ViewModels;

namespace AlphaBetaPro.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _shutdownComplete;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!_shutdownComplete)
        {
            e.Cancel = true;
            IsEnabled = false;
            await _viewModel.ShutdownAsync();
            _shutdownComplete = true;
            Close();
            return;
        }

        base.OnClosing(e);
    }
}
