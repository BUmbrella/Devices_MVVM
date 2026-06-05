using Devices_MVVM.Devices.Models;
using MvCameraControl;
using HikDevice = MvCameraControl.IDevice;

namespace Devices_MVVM.Devices.Cameras;

public sealed class HikAreaCameraDevice : HikCameraDeviceBase
{
    public HikAreaCameraDevice(DeviceDefinition definition)
        : base(definition)
    {
       
    }
}
