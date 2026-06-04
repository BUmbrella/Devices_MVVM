using System.Windows;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Services;
using Devices_MVVM.ViewModels;
using Prism.DryIoc;
using Prism.Ioc;

namespace Devices_MVVM;

public partial class App : PrismApplication
{
    protected override Window CreateShell()
    {
        var window = Container.Resolve<MainWindow>();
        window.Loaded += OnShellLoaded;

        return window;

        async void OnShellLoaded(object sender, RoutedEventArgs e)
        {
            window.Loaded -= OnShellLoaded;

            if (window.DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        }
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<IDeviceLogService, DeviceLogService>();
        containerRegistry.RegisterSingleton<IDeviceFactory, DeviceFactory>();
        containerRegistry.RegisterSingleton<IDeviceManager, DeviceManager>();
        containerRegistry.RegisterSingleton<DeviceBootstrapper>();
    }
}
