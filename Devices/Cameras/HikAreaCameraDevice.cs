using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;
using Devices_MVVM.Services;

namespace Devices_MVVM.Devices.Cameras;

public sealed class HikAreaCameraDevice : DeviceBase, ICameraDevice
{
    public HikAreaCameraDevice(DeviceDefinition definition)
        : base(definition)
    {
    }

    public string SerialNumber => GetRequiredSetting("SerialNumber");
    public double ExposureTime => GetDoubleSetting("ExposureTime");
    public double Gain => GetDoubleSetting("Gain");

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        SetStatus(DeviceStatus.Initializing);

        // 这里以后接海康 SDK: 用 SerialNumber 找相机，再把 ExposureTime/Gain 写入相机参数。
        await Task.Delay(50, cancellationToken);

        SetStatus(DeviceStatus.Ready);
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(30, cancellationToken);
        SetStatus(DeviceStatus.Running);
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(30, cancellationToken);
        SetStatus(DeviceStatus.Stopped);
    }

    public async Task TakePhotoAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(30, cancellationToken);
    }
}
