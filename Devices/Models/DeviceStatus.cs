namespace Devices_MVVM.Devices.Models;

public enum DeviceStatus
{
    Unknown = 0,
    Created = 1,
    Initializing = 2,
    Ready = 3,
    Running = 4,
    Stopped = 5,
    Faulted = 6,
    Disconnected = 7
}
