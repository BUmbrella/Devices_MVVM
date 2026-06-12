using Devices_MVVM.Devices.Interfaces;

namespace Devices_MVVM.Main;

/// <summary>
/// 设备主流程服务。
/// ViewModel 只负责按钮和界面状态；这里负责创建/销毁每一轮主流程会话。
/// </summary>
public sealed class MainProcessService
{
    private readonly IDeviceLogService _deviceLogService;

    public MainProcessService(IDeviceLogService deviceLogService)
    {
        _deviceLogService = deviceLogService;
    }

    /// <summary>
    /// 主流程状态变化事件。ViewModel 订阅后更新 StatusText。
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// 主流程图片更新事件。后面接相机或算法时，可以把图片路径传给界面显示。
    /// 当前先保留接口，避免以后把 UI 更新逻辑写进主流程。
    /// </summary>
    public event EventHandler<string>? ImagePathUpdated;

    /// <summary>
    /// 启动并运行主流程。
    /// 外部停止时只需要调用 CancellationTokenSource.Cancel()。
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using MainProcessSession session = new MainProcessSession(_deviceLogService);

        session.StatusChanged += (_, status) => StatusChanged?.Invoke(this, status);
        session.ImagePathUpdated += (_, imagePath) => ImagePathUpdated?.Invoke(this, imagePath);

        await session.RunAsync(cancellationToken);
    }
}
