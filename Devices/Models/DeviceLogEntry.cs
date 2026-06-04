namespace Devices_MVVM.Devices.Models;

public sealed class DeviceLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string DeviceId { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string Level { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
   
}
