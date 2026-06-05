using System.Collections.ObjectModel;
using System.Windows;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;
using Devices_MVVM.Services;
using Prism.Commands;
using Prism.Mvvm;

namespace Devices_MVVM.ViewModels;

public sealed class MainWindowViewModel : BindableBase
{
    private readonly DeviceBootstrapper _deviceBootstrapper;
    private readonly IDeviceLogService _deviceLogService;
    
    private string _statusText = "未初始化";

    public MainWindowViewModel(
        DeviceBootstrapper deviceBootstrapper,
        IDeviceLogService deviceLogService)
    {
        _deviceBootstrapper = deviceBootstrapper;
        _deviceLogService = deviceLogService;

        
        //订阅所有设备触发日志
        _deviceLogService.LogAdded += OnLogAdded;
    }
    //ObservableCollection<string> 的作用是：集合内容变化时，会自动通知界面刷新。
    public ObservableCollection<string> Logs { get; } = new();
    

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

  

    public async Task InitializeAsync()
    {
        try
        {
            StatusText = "正在初始化...";

            await _deviceBootstrapper.InitializeAsync();

            StatusText = "初始化完成";
        }
        catch (Exception ex)
        {
            StatusText = "初始化失败";
            _deviceLogService.Info("System", $"设备初始化失败，异常: {ex}");
        }
       
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="log"></param>
    private void OnLogAdded(object? sender, DeviceLogEntry log)
    {
        string logText = $"[{log.Timestamp:HH:mm:ss}] [{log.Level}] {log.DeviceName} {log.Message}";

        //拿到 WPF 主界面的调度器
        var dispatcher = Application.Current.Dispatcher;
        //检查当前代码是不是在 UI 线程。
        if (dispatcher.CheckAccess())
        {
            AddLogToUi(logText);
            return;
        }

        dispatcher.Invoke(() => AddLogToUi(logText));
    }

    private void AddLogToUi(string logText)
    {
        Logs.Insert(0, logText);

        //超过200的日志就舍去
        while (Logs.Count > 200)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }
}
