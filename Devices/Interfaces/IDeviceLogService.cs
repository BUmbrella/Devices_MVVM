using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Devices.Interfaces;

public interface IDeviceLogService
{
    event EventHandler<DeviceLogEntry>? LogAdded;

    void Info(string deviceId, string message, string? deviceName = null);
    void Warn(string deviceId, string message, string? deviceName = null);
    void Error(string deviceId, string message, Exception? exception = null, string? deviceName = null);
}
