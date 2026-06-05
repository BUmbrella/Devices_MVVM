using System.Windows;
using System.ComponentModel;
using Devices_MVVM.Devices.Interfaces;

namespace Devices_MVVM.Views;

public partial class MainWindow : Window
{
    private readonly IDeviceManager _deviceManager;
    private bool _isConfirmedClosing;
    private bool _isExitConfirming;

    public MainWindow(IDeviceManager deviceManager)
    {
        _deviceManager = deviceManager;
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isConfirmedClosing)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (_isExitConfirming)
        {
            return;
        }

        _isExitConfirming = true;
        _ = Dispatcher.BeginInvoke(new Action(async () => await ConfirmExitAsync()));
    }

    private async Task ConfirmExitAsync()
    {
        MessageBoxResult result = MessageBox.Show(
            "是否退出软件？",
            "确认退出",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            _isExitConfirming = false;
            return;
        }

        try
        {
            IsEnabled = false;
            await _deviceManager.StopAllAsync();
        }
        finally
        {
            _isConfirmedClosing = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }
}
