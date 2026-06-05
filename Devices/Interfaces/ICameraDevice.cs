namespace Devices_MVVM.Devices.Interfaces;

public interface ICameraDevice : IDevice
{
    Task TakePhotoAsync(CancellationToken cancellationToken = default);
}

public enum CameraCaptureMode
{
    // 单一采集模式：Start 后等待软件触发拍一张。
    SingleShot = 0,

    // 视频/实时预览模式：Start 后持续取流。
    Video = 1
}
