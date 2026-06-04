using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Devices.Interfaces;

public interface IDeviceLogService
{
    event EventHandler<DeviceLogEntry>? LogAdded;

    void Info(string deviceId, string message, string? deviceName = null);
}
