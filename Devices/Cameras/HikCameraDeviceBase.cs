using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;
using Devices_MVVM.Services;
using MvCameraControl;
using HikDevice = MvCameraControl.IDevice;

namespace Devices_MVVM.Devices.Cameras;

public abstract class HikCameraDeviceBase : DeviceBase, ICameraDevice
{
    private HikDevice? _camera;
    private Thread? _grabThread;
    private bool _isGrabbing;
    private bool _streamStarted;

    protected HikCameraDeviceBase(DeviceDefinition definition)
        : base(definition)
    {
        SerialNumber = GetRequiredSetting("SerialNumber");
        CaptureMode = GetCaptureMode();
    }

    public string SerialNumber { get; }
    public CameraCaptureMode CaptureMode { get; }

    public override Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetStatus(DeviceStatus.Initializing);

        List<IDeviceInfo> cameras = EnumerateCameras();
        IDeviceInfo cameraInfo = cameras.FirstOrDefault(c => c.SerialNumber == SerialNumber)
            ?? throw new InvalidOperationException($"设备 {Name} 未找到序列号为 {SerialNumber} 的海康相机");

        _camera = MvCameraControl.DeviceFactory.CreateDevice(cameraInfo);

        int ret = _camera.Open();
        if (ret != MvError.MV_OK)
        {
            _camera.Dispose();
            _camera = null;
            throw new InvalidOperationException($"设备 {Name} 打开海康相机失败: {ret}");
        }

        ConfigureCamera(_camera);
        SetStatus(DeviceStatus.Ready);
        return Task.CompletedTask;
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_camera == null)
        {
            throw new InvalidOperationException($"设备 {Name} 尚未初始化");
        }

        StartStream();
        _isGrabbing = true;

        if (CaptureMode == CameraCaptureMode.Video)
        {
            StartGrabThread();
        }

        SetStatus(DeviceStatus.Running);
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DestroyCamera();
        SetStatus(DeviceStatus.Stopped);
        return Task.CompletedTask;
    }

    public Task TakePhotoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_camera == null || !_isGrabbing)
        {
            throw new InvalidOperationException($"设备 {Name} 未开始采集");
        }

        // 软件触发一次，单帧模式下就是拍一张；视频模式下也可以按需抓取当前触发帧。
        _camera.Parameters.SetCommandValue("TriggerSoftware");
        return Task.CompletedTask;
    }

    protected virtual void ConfigureCamera(HikDevice camera)
    {
        // 连续采集模式：相机进入持续取流状态，具体出图由触发方式控制。
        camera.Parameters.SetEnumValueByString("AcquisitionMode", "Continuous");

        // 关闭帧率限制，让相机不受 AcquisitionFrameRate 限制。
        camera.Parameters.SetBoolValue("AcquisitionFrameRateEnable", false);

        // 关闭自动曝光，曝光时间由具体相机配置手动设置。
        camera.Parameters.SetEnumValue("ExposureAuto", 0);

        // 打开触发模式。
        camera.Parameters.SetEnumValueByString("TriggerMode", "On");

        // 选择帧开始触发，也就是每次触发采集一帧。
        camera.Parameters.SetEnumValueByString("TriggerSelector", "FrameStart");

        // 使用软件触发，后续拍照时调用 TriggerSoftware。
        camera.Parameters.SetEnumValueByString("TriggerSource", "Software");

        // 触发延时设为 0，收到软件触发后立即采集。
        camera.Parameters.SetFloatValue("TriggerDelay", 0);
    }

    protected void SetFloatParameter(string key, double value)
    {
        if (_camera == null)
        {
            throw new InvalidOperationException($"设备 {Name} 尚未初始化");
        }

        _camera.Parameters.SetFloatValue(key, (float)value);
    }

    private static List<IDeviceInfo> EnumerateCameras()
    {
        int ret = DeviceEnumerator.EnumDevices(
            DeviceTLayerType.MvGigEDevice | DeviceTLayerType.MvUsbDevice,
            out List<IDeviceInfo> devices);

        if (ret != MvError.MV_OK)
        {
            throw new InvalidOperationException($"枚举海康相机失败: {ret}");
        }

        return devices;
    }

    private void StartStream()
    {
        if (_camera == null || _streamStarted)
        {
            return;
        }

        int ret = _camera.StreamGrabber.StartGrabbing();
        if (ret != MvError.MV_OK)
        {
            throw new InvalidOperationException($"设备 {Name} 开始采集失败: {ret}");
        }

        _streamStarted = true;
    }

    private void StartGrabThread()
    {
        if (_grabThread is { IsAlive: true })
        {
            return;
        }

        _grabThread = new Thread(GrabLoop)
        {
            IsBackground = true,
            Name = $"{Name}-GrabThread"
        };
        _grabThread.Start();
    }

    private void GrabLoop()
    {
        while (_isGrabbing && _camera != null)
        {
            int ret = _camera.StreamGrabber.GetImageBuffer(1000, out var frame);
            if (ret != MvError.MV_OK || frame == null)
            {
                continue;
            }

            _camera.StreamGrabber.FreeImageBuffer(frame);
        }
    }

    private CameraCaptureMode GetCaptureMode()
    {
        string mode = GetOptionalSetting("CaptureMode", "SingleShot");

        return mode.Equals("Video", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("Preview", StringComparison.OrdinalIgnoreCase)
            ? CameraCaptureMode.Video
            : CameraCaptureMode.SingleShot;
    }

    private void DestroyCamera()
    {
        _isGrabbing = false;
        _grabThread?.Join(500);
        _grabThread = null;

        try
        {
            if (_streamStarted)
            {
                _camera?.StreamGrabber.StopGrabbing();
            }
        }
        catch
        {
        }

        _camera?.Close();
        _camera?.Dispose();
        _camera = null;
        _streamStarted = false;
    }
}
