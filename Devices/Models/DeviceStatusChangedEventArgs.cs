namespace Devices_MVVM.Devices.Models;

public sealed class DeviceStatusChangedEventArgs : EventArgs
{
    public DeviceStatusChangedEventArgs(DeviceStatus oldStatus, DeviceStatus newStatus)
    {
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }

    public DeviceStatus OldStatus { get; }
    public DeviceStatus NewStatus { get; }
}
