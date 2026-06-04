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
    private readonly IDeviceStateStore _deviceStateStore;
    private string _statusText = "未初始化";
    private bool _isInitializing;

    public MainWindowViewModel(
        DeviceBootstrapper deviceBootstrapper,
        IDeviceLogService deviceLogService,
        IDeviceStateStore deviceStateStore)
    {
        _deviceBootstrapper = deviceBootstrapper;
        _deviceLogService = deviceLogService;
        _deviceStateStore = deviceStateStore;

        InitializeCommand = new DelegateCommand(
            async () => await InitializeAsync(),
            () => !IsInitializing);

        _deviceLogService.LogAdded += OnLogAdded;
        _deviceStateStore.StateChanged += OnStateChanged;
    }

    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<DeviceRuntimeState> States { get; } = new();
    public DelegateCommand InitializeCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsInitializing
    {
        get => _isInitializing;
        private set
        {
            if (SetProperty(ref _isInitializing, value))
            {
                InitializeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            IsInitializing = true;
            StatusText = "正在初始化...";

            await _deviceBootstrapper.InitializeAsync();

            StatusText = "初始化完成";
        }
        catch (Exception ex)
        {
            StatusText = "初始化失败";
            _deviceLogService.Error("System", "设备初始化失败", ex);
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private void OnLogAdded(object? sender, DeviceLogEntry log)
    {
        RunOnUiThread(() =>
        {
            Logs.Insert(0, $"[{log.Timestamp:HH:mm:ss}] [{log.Level}] {log.DeviceName} {log.Message}");
        });
    }

    private void OnStateChanged(object? sender, DeviceRuntimeState state)
    {
        RunOnUiThread(() =>
        {
            var existing = States.FirstOrDefault(item => item.DeviceId == state.DeviceId);
            if (existing == null)
            {
                States.Add(state);
                return;
            }

            var index = States.IndexOf(existing);
            States[index] = state;
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
