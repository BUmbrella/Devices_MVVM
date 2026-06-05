using System.IO;
using System.Text;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Services;
/// <summary>
/// 管理整个项目的日志
/// </summary>
public sealed class DeviceLogService : IDeviceLogService
{
    private readonly object _fileLock = new();
    private readonly string _logFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
    private readonly TimeSpan _logRetention = TimeSpan.FromDays(31);

    public event EventHandler<DeviceLogEntry>? LogAdded;
    /// <summary>
    ///汇总整个设备的日志
    /// </summary>
    /// <param name="deviceId"></param>
    /// <param name="message"></param>
    /// <param name="deviceName"></param>
    public void Info(string deviceId, string message, string? deviceName = null)
    {
        var log = new DeviceLogEntry
        {
            DeviceId = deviceId,
            DeviceName = deviceName ?? string.Empty,
            Level = "Info",
            Message = message
        };

        Save(log);
        LogAdded?.Invoke(this, log);
    }

    private void Save(DeviceLogEntry log)
    {
        try
        {
            Directory.CreateDirectory(_logFolderPath);
            DeleteExpiredLogs();

            string logPath = Path.Combine(_logFolderPath, $"{log.Timestamp:M.d}");
            string logText = $"[{log.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{log.Level}] [{log.DeviceId}] {log.DeviceName} {Normalize(log.Message)}";

            lock (_fileLock)
            {
                File.AppendAllText(logPath, logText + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志保存失败不能影响设备业务流程。
        }
    }


    private static string Normalize(string value)
    {
        return value.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private void DeleteExpiredLogs()
    {
        DateTime expiredBefore = DateTime.Now.Subtract(_logRetention);

        foreach (string filePath in Directory.EnumerateFiles(_logFolderPath))
        {
            var fileInfo = new FileInfo(filePath);

            if (fileInfo.LastWriteTime < expiredBefore)
            {
                fileInfo.Delete();
            }
        }
    }
}
