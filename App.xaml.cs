using System.Windows;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Services;
using Prism.DryIoc;
using Prism.Ioc;

namespace Devices_MVVM;

public partial class App : PrismApplication
{
    protected override Window CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<IDeviceLogService, DeviceLogService>();
        containerRegistry.RegisterSingleton<IDeviceStateStore, DeviceStateStore>();
        containerRegistry.RegisterSingleton<IDeviceFactory, DeviceFactory>();
        containerRegistry.RegisterSingleton<IDeviceManager, DeviceManager>();
        containerRegistry.RegisterSingleton<DeviceBootstrapper>();
    }
}
