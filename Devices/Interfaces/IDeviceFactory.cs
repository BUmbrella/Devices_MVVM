using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Devices.Interfaces;

public interface IDeviceFactory
{
    IDevice Create(DeviceDefinition definition);
}
