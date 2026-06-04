using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Devices.Interfaces;

public interface IDeviceConfigurationLoader
{
    IReadOnlyList<DeviceDefinition> Load();
}
