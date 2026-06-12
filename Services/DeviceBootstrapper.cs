using System.IO;
using System.Text.Json;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Services;
/// <summary>
/// 创建所有的设备
/// </summary>
public sealed class DeviceBootstrapper
{
    private readonly IDeviceManager _deviceManager;
    private readonly IDeviceLogService _deviceLogService;
    private readonly IDeviceFactory _factory;
    private readonly List<DeviceDefinition> _definitions = new();

    public DeviceBootstrapper(
        IDeviceManager deviceManager,
        IDeviceLogService deviceLogService,
        IDeviceFactory factory)
    {
        _deviceManager = deviceManager;
        _deviceLogService = deviceLogService;
        _factory = factory;
    }

    public async Task<DeviceBootstrapResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        ReadFile();

        var initializeTasks = _definitions
            .Select(definition => Task.Run(() => InitializeDeviceAsync(definition, cancellationToken), cancellationToken))
            .ToArray();

        DeviceInitializeResult[] results = await Task.WhenAll(initializeTasks);

        int createdCount = results.Count(result => result.Created);
        int failedCount = results.Count(result => result.Failed);
        int skippedCount = results.Count(result => result.Skipped);

        _deviceLogService.Info(
            "System",
            $"设备初始化结束，成功: {createdCount}，失败: {failedCount}，跳过: {skippedCount}");

        return new DeviceBootstrapResult(results);
    }

    private async Task<DeviceInitializeResult> InitializeDeviceAsync(
        DeviceDefinition definition,
        CancellationToken cancellationToken)
    {
        if (definition.SkipCreate)
        {
            _deviceLogService.Info(definition.Id, $"已跳过创建设备: {definition.Name}", definition.Name);
            return DeviceInitializeResult.Skip(definition.Id, definition.Name, definition.Kind);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var device = _factory.Create(definition);
            if (!_deviceManager.Register(device))
            {
                return DeviceInitializeResult.Skip(definition.Id, definition.Name, definition.Kind);
            }

            _deviceLogService.Info(device.Id, $"设备已创建: {device.Name}", device.Name);

            await _deviceManager.InitializeAsync(device.Id, cancellationToken);
            await _deviceManager.StartAsync(device.Id, cancellationToken);

            return DeviceInitializeResult.Create(definition.Id, definition.Name, definition.Kind);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _deviceLogService.Info(
                definition.Id,
                $"设备创建或初始化失败: {definition.Name} ({definition.Kind})，异常: {ex}",
                definition.Name);

            return DeviceInitializeResult.Fail(definition.Id, definition.Name, definition.Kind, ex.Message);
        }
    }

    private void ReadFile()
    {
        _definitions.Clear();

        string startupPath = AppDomain.CurrentDomain.BaseDirectory;
        string dataFolder = Path.Combine(startupPath, "data");
        string configPath = Path.Combine(dataFolder, "device_config.json");

        if (!Directory.Exists(dataFolder))
        {
            Directory.CreateDirectory(dataFolder);
        }

        if (!File.Exists(configPath))
        {
            CreateSampleConfig(configPath);
            _deviceLogService.Info("System", $"配置文件不存在，已创建示例配置: {configPath}");
        }

        try
        {
            string json = File.ReadAllText(configPath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var root = JsonSerializer.Deserialize<DeviceConfigRoot>(json, options);

            if (root?.Devices == null || root.Devices.Count == 0)
            {
                _deviceLogService.Info("System", "设备配置为空");
                return;
            }

            foreach (var definition in root.Devices)
            {
                _definitions.Add(definition);
                _deviceLogService.Info(definition.Id, $"加载设备: {definition.Name}", definition.Name);
            }
        }
        catch (Exception ex)
        {
            _deviceLogService.Info("System", $"读取配置失败，异常: {ex}");
        }
    }

    private static void CreateSampleConfig(string configPath)
    {
        var sample = new DeviceConfigRoot
        {
            Devices =
            [
                new DeviceDefinition
                {
                    Id = "cam1",
                    Name = "Liftcamera",
                    DeviceType = DeviceType.Camera,
                    Kind = "HikArea",
                    SkipCreate = false,
                    Settings = new Dictionary<string, string>
                    {
                        ["SerialNumber"] = "00J16501440",
                        ["ExposureTime"] = "3000",
                        ["Gain"] = "8"
                    }
                },
                new DeviceDefinition
                {
                    Id = "cam2",
                    Name = "Rightcamera",
                    DeviceType = DeviceType.Camera,
                    Kind = "HikArea",
                    SkipCreate = true,
                    Settings = new Dictionary<string, string>
                    {
                        ["SerialNumber"] = "DA8228005",
                        ["ExposureTime"] = "5000",
                        ["Gain"] = "12"
                    }
                },
                new DeviceDefinition
                {
                    Id = "modbus_01",
                    Name = "Modbus TCP 1",
                    DeviceType = DeviceType.Communication,
                    Kind = "ModbusTcp",
                    SkipCreate = true,
                    Settings = new Dictionary<string, string>
                    {
                        ["IpAddress"] = "192.168.1.10",
                        ["Port"] = "502"
                    }
                },
                new DeviceDefinition
                {
                    Id = "serial_01",
                    Name = "Serial Port 1",
                    DeviceType = DeviceType.Communication,
                    Kind = "SerialPort",
                    SkipCreate = true,
                    Settings = new Dictionary<string, string>
                    {
                        ["PortName"] = "COM1",
                        ["BaudRate"] = "9600"
                    }
                },
                new DeviceDefinition
                {
                    Id = "db_01",
                    Name = "SqlServer 1",
                    DeviceType = DeviceType.Database,
                    Kind = "SqlServer",
                   SkipCreate = true,
                    Settings = new Dictionary<string, string>
                    {
                        ["ConnectionString"] = "Server=.;Database=Demo;Trusted_Connection=True;"
                    }
                },
                new DeviceDefinition
                {
                    Id = "db_02",
                    Name = "SQLite 2",
                    DeviceType = DeviceType.Database,
                    Kind = "SQLite",
                    SkipCreate = true,
                    Settings = new Dictionary<string, string>
                    {
                        ["DatabasePath"] = "data/demo.db"
                    }
                }
            ]
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        File.WriteAllText(configPath, JsonSerializer.Serialize(sample, options));
    }

    private sealed class DeviceConfigRoot
    {
        public List<DeviceDefinition> Devices { get; set; } = new();
    }

    public sealed class DeviceBootstrapResult
    {
        public DeviceBootstrapResult(IReadOnlyList<DeviceInitializeResult> deviceResults)
        {
            DeviceResults = deviceResults;
        }

        public IReadOnlyList<DeviceInitializeResult> DeviceResults { get; }

        public IReadOnlyList<DeviceInitializeResult> FailedDevices =>
            DeviceResults.Where(result => result.Failed).ToArray();

        public bool HasFailedDevice => FailedDevices.Count > 0;
    }

    public sealed record DeviceInitializeResult(
        string DeviceId,
        string DeviceName,
        string Kind,
        bool Created,
        bool Failed,
        bool Skipped,
        string? ErrorMessage)
    {
        public static DeviceInitializeResult Create(string deviceId, string deviceName, string kind)
        {
            return new DeviceInitializeResult(deviceId, deviceName, kind, true, false, false, null);
        }

        public static DeviceInitializeResult Fail(string deviceId, string deviceName, string kind, string errorMessage)
        {
            return new DeviceInitializeResult(deviceId, deviceName, kind, false, true, false, errorMessage);
        }

        public static DeviceInitializeResult Skip(string deviceId, string deviceName, string kind)
        {
            return new DeviceInitializeResult(deviceId, deviceName, kind, false, false, true, null);
        }
    }
}
