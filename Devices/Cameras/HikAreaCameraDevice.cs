using Devices_MVVM.Devices.Models;
using MvCameraControl;
using HikDevice = MvCameraControl.IDevice;

namespace Devices_MVVM.Devices.Cameras;

public sealed class HikAreaCameraDevice : HikCameraDeviceBase
{
    public HikAreaCameraDevice(DeviceDefinition definition)
        : base(definition)
    {
        ExposureTime = GetDoubleSetting("ExposureTime");
        Gain = GetDoubleSetting("Gain");
    }

    public double ExposureTime { get; }
    public double Gain { get; }

    protected override void ConfigureCamera(HikDevice camera)
    {
        base.ConfigureCamera(camera);

        // 设置手动曝光时间，单位跟海康 SDK 参数一致，通常是微秒。
        camera.Parameters.SetFloatValue("ExposureTime", (float)ExposureTime);

        // 关闭自动增益，增益由配置文件手动指定。
        camera.Parameters.SetEnumValue("GainAuto", 0);

        // 设置手动增益。
        camera.Parameters.SetFloatValue("Gain", (float)Gain);
    }
}
