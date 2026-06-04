using System.Collections.Concurrent;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Services;

public sealed class DeviceManager : IDeviceManager
{
    private readonly ConcurrentDictionary<string, IDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDeviceLogService _deviceLogService;

    public DeviceManager(IDeviceLogService deviceLogService)
    {
        _deviceLogService = deviceLogService;
    }

    public IReadOnlyCollection<IDevice> Devices => _devices.Values.ToArray();

    public bool Register(IDevice device)
    {
        if (!_devices.TryAdd(device.Id, device))
        {
            _deviceLogService.Info(device.Id, $"重复设备，已跳过: {device.Name}", device.Name);
            return false;
        }

        _deviceLogService.Info(device.Id, $"已注册设备: {device.Name}", device.Name);
        return true;
    }
    /// <summary>
    ///  这里就是根据设备注册时的ID去拿到对应的设备，必须在注册之后使用
    /// </summary>
    /// <param name="deviceId"></param>
    /// <returns></returns>
    /// <exception cref="KeyNotFoundException"></exception>
    public IDevice Get(string deviceId)
    {
        if (_devices.TryGetValue(deviceId, out var device))
        {
            return device;
        }

        throw new KeyNotFoundException($"未找到设备: {deviceId}");
    }

    public Task InitializeAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        return ExecuteDeviceActionAsync(Get(deviceId), d => d.InitializeAsync(cancellationToken));
    }

    public Task StartAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        return ExecuteDeviceActionAsync(Get(deviceId), d => d.StartAsync(cancellationToken));
    }

    public Task StopAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        return ExecuteDeviceActionAsync(Get(deviceId), d => d.StopAsync(cancellationToken));
    }


    /// <summary>
    /// 对所有的设备给外部都定义了一个委托，就是拿到这个设备去调用他自己方法
    /// </summary>
    /// <param name="device"></param>
    /// <param name="action"></param>
    /// <returns></returns>
    private async Task ExecuteDeviceActionAsync(IDevice device, Func<IDevice, Task> action)
    {
        try
        {
            await action(device);
        }
        catch (Exception exception)
        {
            _deviceLogService.Info(device.Id, $"设备 {device.Name} 操作失败，异常: {exception}", device.Name);
            throw;
        }
    }

}
