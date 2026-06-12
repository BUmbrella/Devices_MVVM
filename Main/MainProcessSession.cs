using System.Collections.Concurrent;
using System.IO;
using Devices_MVVM.Devices.Interfaces;

namespace Devices_MVVM.Main;

/// <summary>
/// 一次“启动 -> 运行 -> 停止”的主流程会话。
/// 
/// 为什么用这个类：
/// 1. 每次点击启动就 new 一个 MainProcessSession。
/// 2. 主流程里的临时变量全部放在这个类的字段里。
/// 3. 停止时 Dispose 这个类，本轮临时状态就统一清掉。
/// 4. 下一次启动再 new 一个新的，天然不会读到上一轮残留。
/// </summary>
public sealed class MainProcessSession : IDisposable
{
    private readonly IDeviceLogService _deviceLogService;
    private readonly ConcurrentDictionary<string, object> _temporaryValues = new();
    private readonly List<string> _temporaryFiles = new();
    private readonly object _fileLock = new();
    private bool _disposed;

    // 下面这些就是主流程临时变量示例。
    // 后续你需要什么临时属性，就直接加成字段/属性，不用到处传 context。
    private int _loopCount;

    public MainProcessSession(
        IDeviceLogService deviceLogService)
    {
        _deviceLogService = deviceLogService;
        RunId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        StartedAt = DateTime.Now;
    }

    public string RunId { get; }

    public DateTime StartedAt { get; }

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<string>? ImagePathUpdated;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
        {
            NotifyStatus("正在启动主流程...");
            _deviceLogService.Info("Main", $"主流程启动，RunId={RunId}");

            NotifyStatus("主流程运行中");
            await RunMainLoopAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _deviceLogService.Info("Main", $"主流程收到停止信号，RunId={RunId}");
            NotifyStatus("正在停止主流程...");
        }
        catch (Exception ex)
        {
            _deviceLogService.Info("Main", $"主流程异常，RunId={RunId}，异常: {ex}");
            NotifyStatus("主流程异常停止");
            throw;
        }
        finally
        {
            _deviceLogService.Info("Main", $"主流程已退出，RunId={RunId}");
            NotifyStatus("已停止");
        }
    }

    private async Task RunMainLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _loopCount++;

            // TODO: 真正主流程写这里。
            // 你后面要加临时变量，就直接写成这个类里的字段：
            // private Mat? _currentImage;
            // private string? _currentProductSn;
            // private List<YoloResult> _currentBoxes = new();
            //
            // 停止时 Dispose() 会统一清掉这些字段。

            if (_loopCount == 1 || _loopCount % 50 == 0)
            {
                _deviceLogService.Info("Main", $"主流程心跳: {_loopCount}");
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    public void SetValue(string key, object value)
    {
        ThrowIfDisposed();
        _temporaryValues[key] = value;
    }

    public bool TryGetValue<T>(string key, out T? value)
    {
        ThrowIfDisposed();

        if (_temporaryValues.TryGetValue(key, out object? rawValue) && rawValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }

    public void AddTemporaryFile(string filePath)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        lock (_fileLock)
        {
            _temporaryFiles.Add(filePath);
        }
    }

    private void NotifyStatus(string status)
    {
        StatusChanged?.Invoke(this, status);
    }

    public void NotifyImagePath(string imagePath)
    {
        ImagePathUpdated?.Invoke(this, imagePath);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _temporaryValues.Clear();
        _loopCount = 0;

        lock (_fileLock)
        {
            foreach (string filePath in _temporaryFiles)
            {
                TryDeleteFile(filePath);
            }

            _temporaryFiles.Clear();
        }

        // 后续如果加了 Mat、模型对象、文件流、缓存队列，也在这里统一 Dispose / Clear。
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // 临时文件清理失败不能影响主流程停止。
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MainProcessSession));
        }
    }
}
