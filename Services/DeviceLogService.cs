using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Services;

public sealed class DeviceLogService : IDeviceLogService
{
    public event EventHandler<DeviceLogEntry>? LogAdded;

    public void Info(string deviceId, string message, string? deviceName = null)
    {
        LogAdded?.Invoke(
            this,
            new DeviceLogEntry
            {
                DeviceId = deviceId,
                DeviceName = deviceName ?? string.Empty,
                Level = "Info",
                Message = message
            });
    }
}
