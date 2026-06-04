using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Services;

public sealed class DeviceLogService : IDeviceLogService
{
    public event EventHandler<DeviceLogEntry>? LogAdded;

    public void Info(string deviceId, string message, string? deviceName = null)
    {
        Add("Info", deviceId, message, null, deviceName);
    }

    public void Warn(string deviceId, string message, string? deviceName = null)
    {
        Add("Warn", deviceId, message, null, deviceName);
    }

    public void Error(string deviceId, string message, Exception? exception = null, string? deviceName = null)
    {
        Add("Error", deviceId, message, exception, deviceName);
    }

    private void Add(string level, string deviceId, string message, Exception? exception, string? deviceName)
    {
        LogAdded?.Invoke(
            this,
            new DeviceLogEntry
            {
                DeviceId = deviceId,
                DeviceName = deviceName ?? string.Empty,
                Level = level,
                Message = message,
                Exception = exception?.ToString()
            });
    }
}
