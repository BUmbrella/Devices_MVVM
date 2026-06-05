using System.Windows;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Services;
using Devices_MVVM.ViewModels;
using Devices_MVVM.Views;
using Prism.DryIoc;
using Prism.Ioc;

namespace Devices_MVVM;

public partial class App : PrismApplication
{
    //CreateShell() 是 Prism 提供的生命周期方法。Prism 启动时会调用它，用来创建应用程序的主窗口，也就是 Shell
    protected override Window CreateShell()

    {//从 Prism 的依赖注入容器里创建 MainWindow->mainview->看到需要另外两个接口，所以去创建接口->注入接口创建主界面
        return Container.Resolve<MainWindow>();
    }

    protected override async void OnInitialized()
    {
        base.OnInitialized();

        if (Current.MainWindow?.DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeAsync();
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
