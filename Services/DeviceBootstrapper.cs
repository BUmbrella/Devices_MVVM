using System.IO;
using System.Text.Json;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Services;

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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ReadFile();

        foreach (var definition in _definitions)
        {
            //这样子是为了避免任何一个设备创建不会影响整体设备的创建  不会直接抛出异常以后
            try
            {//先要进行创建，
                var device = _factory.Create(definition);
                //然后再去进行注册
                if (_deviceManager.Register(device))
                {
                    _deviceLogService.Info(device.Id, $"设备已创建: {device.Name}", device.Name);
                    await _deviceManager.InitializeAsync(device.Id, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _deviceLogService.Info(
                    definition.Id,
                    $"设备创建或初始化失败: {definition.Name} ({definition.Kind})，异常: {ex}",
                    definition.Name);
            }
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
                    Name = "左工位相机",
                    DeviceType = DeviceType.Camera,
                    Kind = "HikArea",
                    Settings = new Dictionary<string, string>
                    {
                        ["SerialNumber"] = "DA8228004",
                        ["ExposureTime"] = "3000",
                        ["Gain"] = "8"
                    }
                },
                new DeviceDefinition
                {
                    Id = "cam2",
                    Name = "右工位相机",
                    DeviceType = DeviceType.Camera,
                    Kind = "HikArea",
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
}
