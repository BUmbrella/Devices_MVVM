namespace Devices_MVVM.Devices.Models;

public sealed class DeviceRuntimeState
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public DeviceType DeviceType { get; set; } = DeviceType.Unknown;
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;
    public bool IsOnline { get; set; }
    public bool IsRunning { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastHeartbeatTime { get; set; }
    public DateTime LastUpdatedTime { get; set; } = DateTime.Now;
    public Dictionary<string, object?> Extensions { get; } = new();
}
