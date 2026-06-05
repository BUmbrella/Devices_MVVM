using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Models;

public sealed class DeviceConfigRoot
{
    public List<DeviceDefinition> Devices { get; set; } = new();
}
