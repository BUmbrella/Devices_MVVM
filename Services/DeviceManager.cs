using System.Collections.Concurrent;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Services;

public sealed class DeviceManager : IDeviceManager
{
    private readonly ConcurrentDictionary<string, IDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDeviceLogService _deviceLogService;
    private readonly IDeviceStateStore _deviceStateStore;

    public DeviceManager(IDeviceLogService deviceLogService, IDeviceStateStore deviceStateStore)
    {
        _deviceLogService = deviceLogService;
        _deviceStateStore = deviceStateStore;
    }

    public IReadOnlyCollection<IDevice> Devices => _devices.Values.ToArray();

    public bool Register(IDevice device)
    {
        if (!_devices.TryAdd(device.Id, device))
        {
            _deviceLogService.Warn(device.Id, $"重复设备，已跳过: {device.Name}", device.Name);
            return false;
        }

        device.StatusChanged += OnDeviceStatusChanged;
        _deviceStateStore.Update(
            device.Id,
            state =>
            {
                state.DeviceName = device.Name;
                state.DeviceType = device.DeviceType;
                state.Status = device.Status;
                state.IsRunning = device.Status == DeviceStatus.Running;
                state.IsOnline = device.Status is DeviceStatus.Ready or DeviceStatus.Running;
                state.LastUpdatedTime = DateTime.Now;
            },
            device.Name,
            device.DeviceType);

        _deviceLogService.Info(device.Id, $"已注册设备: {device.Name}", device.Name);
        return true;
    }

    public IDevice? Get(string deviceId)
    {
        return _devices.TryGetValue(deviceId, out var device) ? device : null;
    }

    public Task InitializeAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        return ExecuteDeviceActionAsync(GetRequiredDevice(deviceId), d => d.InitializeAsync(cancellationToken));
    }

    public Task StartAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        return ExecuteDeviceActionAsync(GetRequiredDevice(deviceId), d => d.StartAsync(cancellationToken));
    }

    public Task StopAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        return ExecuteDeviceActionAsync(GetRequiredDevice(deviceId), d => d.StopAsync(cancellationToken));
    }



    private async Task ExecuteDeviceActionAsync(IDevice device, Func<IDevice, Task> action)
    {
        try
        {
            await action(device);
        }
        catch (Exception exception)
        {
            _deviceLogService.Error(device.Id, $"设备 {device.Name} 操作失败", exception, device.Name);
            _deviceStateStore.Update(
                device.Id,
                state =>
                {
                    state.LastError = exception.Message;
                    state.Status = DeviceStatus.Faulted;
                    state.IsOnline = false;
                    state.IsRunning = false;
                    state.LastUpdatedTime = DateTime.Now;
                },
                device.Name,
                device.DeviceType);
            throw;
        }
    }

    private IDevice GetRequiredDevice(string deviceId)
    {
        return Get(deviceId) ?? throw new KeyNotFoundException($"未找到设备: {deviceId}");
    }

    private void OnDeviceStatusChanged(object? sender, DeviceStatusChangedEventArgs e)
    {
        if (sender is IDevice device)
        {
            _deviceStateStore.Update(
                device.Id,
                state =>
                {
                    state.DeviceName = device.Name;
                    state.DeviceType = device.DeviceType;
                    state.Status = e.NewStatus;
                    state.IsRunning = e.NewStatus == DeviceStatus.Running;
                    state.IsOnline = e.NewStatus is DeviceStatus.Ready or DeviceStatus.Running;

                    if (state.IsOnline)
                    {
                        state.LastHeartbeatTime = DateTime.Now;
                        state.LastError = null;
                    }
                },
                device.Name,
                device.DeviceType);

            _deviceLogService.Info(device.Id, $"状态变更 {e.OldStatus} -> {e.NewStatus}", device.Name);
        }
    }
}
