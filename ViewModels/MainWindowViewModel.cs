using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;
using Devices_MVVM.Models;
using Devices_MVVM.Services;
using Devices_MVVM.Views;
using Prism.Commands;
using Prism.Mvvm;

namespace Devices_MVVM.ViewModels;

public sealed class MainWindowViewModel : BindableBase
{
    private readonly DeviceBootstrapper _deviceBootstrapper;
    private readonly IDeviceLogService _deviceLogService;
    
    private string _statusText = "未初始化";
    private bool _isRunning;
    private bool _isParameterSettingsVisible;
    private string _parameterSettingsSnapshot = string.Empty;

    public MainWindowViewModel(
        DeviceBootstrapper deviceBootstrapper,
        IDeviceLogService deviceLogService)
    {
        _deviceBootstrapper = deviceBootstrapper;
        _deviceLogService = deviceLogService;

        
        //订阅所有设备触发日志
        _deviceLogService.LogAdded += OnLogAdded;

        StartMainProcessCommand = new DelegateCommand(StartMainProcess);
        OpenParameterSettingsCommand = new DelegateCommand(OpenParameterSettings);
        ConfirmParameterSettingsCommand = new DelegateCommand(ConfirmParameterSettings);
        CancelParameterSettingsCommand = new DelegateCommand(CancelParameterSettings);
        ExitParameterSettingsCommand = new DelegateCommand(ExitParameterSettings);
        AddDeviceCommand = new DelegateCommand(AddDevice);
        DeleteDeviceCommand = new DelegateCommand<DeviceParameterItem>(DeleteDevice);
    }
    //ObservableCollection<string> 的作用是：集合内容变化时，会自动通知界面刷新。
    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<DeviceParameterItem> DeviceParameters { get; } = new();
    public ObservableCollection<DeviceParameterGroup> DeviceParameterGroups { get; } = new();
    public DelegateCommand StartMainProcessCommand { get; }
    public DelegateCommand OpenParameterSettingsCommand { get; }
    public DelegateCommand ConfirmParameterSettingsCommand { get; }
    public DelegateCommand CancelParameterSettingsCommand { get; }
    public DelegateCommand ExitParameterSettingsCommand { get; }
    public DelegateCommand AddDeviceCommand { get; }
    public DelegateCommand<DeviceParameterItem> DeleteDeviceCommand { get; }
    

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RaisePropertyChanged(nameof(StartButtonText));
            }
        }
    }

    public string StartButtonText => IsRunning ? "运行中" : "启动";

    public bool IsParameterSettingsVisible
    {
        get => _isParameterSettingsVisible;
        private set => SetProperty(ref _isParameterSettingsVisible, value);
    }

    public async Task InitializeAsync()
    {
        try
        {
            StatusText = "正在初始化...";

            await _deviceBootstrapper.InitializeAsync();

            StatusText = "初始化完成";
        }
        catch (Exception ex)
        {
            StatusText = "初始化失败";
            _deviceLogService.Info("System", $"设备初始化失败，异常: {ex}");
        }
       
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="log"></param>
    private void OnLogAdded(object? sender, DeviceLogEntry log)
    {
        string logText = $"[{log.Timestamp:HH:mm:ss}] [{log.Level}] {log.DeviceName} {log.Message}";

        //拿到 WPF 主界面的调度器
        var dispatcher = Application.Current.Dispatcher;
        //检查当前代码是不是在 UI 线程。
        if (dispatcher.CheckAccess())
        {
            AddLogToUi(logText);
            return;
        }

        dispatcher.Invoke(() => AddLogToUi(logText));
    }

    private void AddLogToUi(string logText)
    {
        Logs.Insert(0, logText);

        //超过200的日志就舍去
        while (Logs.Count > 200)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private void StartMainProcess()
    {
        if (IsRunning)
        {
            MessageBoxResult result = MessageBox.Show(
                "是否停止设备？",
                "确认停止",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            StopMainProcess();
            IsRunning = false;
            StatusText = "已停止";
            return;
        }

        IsRunning = true;
        StatusText = "运行中";

        // TODO: 在这里编写设备启动后的主流程。
    }

    private void StopMainProcess()
    {
        // TODO: 在这里编写停止设备主流程。
    }

    private void OpenParameterSettings()
    {
        var loginWindow = new LoginWindow
        {
            Owner = Application.Current.MainWindow
        };

        if (loginWindow.ShowDialog() != true)
        {
            return;
        }

        LoadParameterSettings();
        IsParameterSettingsVisible = true;
    }

    private void ConfirmParameterSettings()
    {
        try
        {
            SaveParameterSettings();
            _parameterSettingsSnapshot = ReadConfigJson();
            StatusText = "参数已保存";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"参数保存失败: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelParameterSettings()
    {
        if (string.IsNullOrWhiteSpace(_parameterSettingsSnapshot))
        {
            LoadParameterSettings();
            return;
        }

        ApplyParameterJson(_parameterSettingsSnapshot);
        StatusText = "参数已恢复";
    }

    private void ExitParameterSettings()
    {
        IsParameterSettingsVisible = false;
    }

    private void AddDevice()
    {
        var window = new AddDeviceWindow(CreateDeviceTemplates(), GenerateNextDeviceId)
        {
            Owner = Application.Current.MainWindow
        };

        if (window.ShowDialog() != true || window.DeviceDefinition == null)
        {
            return;
        }

        if (DeviceParameters.Any(device =>
                string.Equals(device.Id, window.DeviceDefinition.Id, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("设备 Id 已存在，请换一个 Id", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DeviceParameters.Add(ToParameterItem(window.DeviceDefinition));
        RebuildParameterGroups();
        StatusText = "已增加设备";
    }

    private void DeleteDevice(DeviceParameterItem? device)
    {
        if (device == null)
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            $"是否删除设备 {device.Name}？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        DeviceParameters.Remove(device);
        RebuildParameterGroups();
        StatusText = "已删除设备";
    }

    private void LoadParameterSettings()
    {
        EnsureConfigFile();
        _parameterSettingsSnapshot = ReadConfigJson();
        ApplyParameterJson(_parameterSettingsSnapshot);
    }

    private void SaveParameterSettings()
    {
        var root = new DeviceConfigRoot
        {
            Devices = DeviceParameterGroups
                .SelectMany(group => group.Devices)
                .Select(ToDefinition)
                .ToList()
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        string json = JsonSerializer.Serialize(root, options);
        File.WriteAllText(GetConfigPath(), json);
    }

    private void ApplyParameterJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var root = JsonSerializer.Deserialize<DeviceConfigRoot>(json, options) ?? new DeviceConfigRoot();

        DeviceParameters.Clear();
        foreach (var definition in root.Devices)
        {
            DeviceParameters.Add(ToParameterItem(definition));
        }

        RebuildParameterGroups();
    }

    private void RebuildParameterGroups()
    {
        DeviceParameterGroups.Clear();

        foreach (var group in DeviceParameters
                     .GroupBy(device => device.DeviceType)
                     .OrderBy(group => GetDeviceTypeOrder(group.Key)))
        {
            var parameterGroup = new DeviceParameterGroup
            {
                DeviceType = group.Key,
                Title = GetDeviceGroupTitle(group.Key)
            };

            foreach (var device in group)
            {
                parameterGroup.Devices.Add(device);
            }

            DeviceParameterGroups.Add(parameterGroup);
        }
    }

    private static int GetDeviceTypeOrder(DeviceType deviceType)
    {
        return deviceType switch
        {
            DeviceType.Camera => 0,
            DeviceType.Communication => 1,
            DeviceType.Database => 2,
            _ => 99
        };
    }

    private static string GetDeviceGroupTitle(DeviceType deviceType)
    {
        return deviceType switch
        {
            DeviceType.Camera => "相机",
            DeviceType.Communication => "Modbus / 通信",
            DeviceType.Database => "数据库",
            _ => "其他设备"
        };
    }

    private string GenerateNextDeviceId(string prefix)
    {
        int index = 1;

        while (DeviceParameters.Any(device =>
                   string.Equals(device.Id, $"{prefix}{index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"{prefix}{index}";
    }
    //创建基础属性
    private static IReadOnlyList<DeviceTemplateOption> CreateDeviceTemplates()
    {
        return
        [
            new DeviceTemplateOption
            {
                DisplayName = "海康面阵相机",
                DeviceType = DeviceType.Camera,
                Kind = "HikArea",
                IdPrefix = "cam",
                Settings = new Dictionary<string, string>
                {
                    ["SerialNumber"] = "",
                    ["ExposureTime"] = "3000",
                    ["Gain"] = "8",
                    ["CaptureMode"] = "SingleShot"
                }
            },
            new DeviceTemplateOption
            {
                DisplayName = "海康线扫相机",
                DeviceType = DeviceType.Camera,
                Kind = "HikLine",
                IdPrefix = "linecam",
                Settings = new Dictionary<string, string>
                {
                    ["SerialNumber"] = "",
                    ["CaptureMode"] = "SingleShot"
                }
            },
            new DeviceTemplateOption
            {
                DisplayName = "Modbus TCP",
                DeviceType = DeviceType.Communication,
                Kind = "ModbusTcp",
                IdPrefix = "modbus",
                Settings = new Dictionary<string, string>
                {
                    ["IpAddress"] = "192.168.1.10",
                    ["Port"] = "502",
                    ["SlaveId"] = "1",
                    ["TestAddress"] = "0"
                }
            },
            new DeviceTemplateOption
            {
                DisplayName = "串口设备",
                DeviceType = DeviceType.Communication,
                Kind = "SerialPort",
                IdPrefix = "serial",
                Settings = new Dictionary<string, string>
                {
                    ["PortName"] = "COM1",
                    ["BaudRate"] = "9600"
                }
            },
            new DeviceTemplateOption
            {
                DisplayName = "MySQL 数据库",
                DeviceType = DeviceType.Database,
                Kind = "MySql",
                IdPrefix = "mysql",
                Settings = new Dictionary<string, string>
                {
                    ["ConnectionString"] = "server=127.0.0.1;user=root;password=root;database=test;charset=utf8;"
                }
            },
            new DeviceTemplateOption
            {
                DisplayName = "SQL Server 数据库",
                DeviceType = DeviceType.Database,
                Kind = "SqlServer",
                IdPrefix = "sqlserver",
                Settings = new Dictionary<string, string>
                {
                    ["ConnectionString"] = "Server=.;Database=Demo;Trusted_Connection=True;"
                }
            },
            new DeviceTemplateOption
            {
                DisplayName = "SQLite 数据库",
                DeviceType = DeviceType.Database,
                Kind = "SQLite",
                IdPrefix = "sqlite",
                Settings = new Dictionary<string, string>
                {
                    ["DatabasePath"] = "data/demo.db"
                }
            }
        ];
    }

    private static DeviceParameterItem ToParameterItem(DeviceDefinition definition)
    {
        var item = new DeviceParameterItem
        {
            Id = definition.Id,
            Name = definition.Name,
            DeviceType = definition.DeviceType,
            Kind = definition.Kind,
            SkipCreate = definition.SkipCreate
        };

        foreach (var setting in definition.Settings)
        {
            item.Settings.Add(new DeviceSettingItem
            {
                Key = setting.Key,
                Value = setting.Value
            });
        }

        return item;
    }

    private static DeviceDefinition ToDefinition(DeviceParameterItem item)
    {
        return new DeviceDefinition
        {
            Id = item.Id,
            Name = item.Name,
            DeviceType = item.DeviceType,
            Kind = item.Kind,
            SkipCreate = item.SkipCreate,
            Settings = item.Settings.ToDictionary(setting => setting.Key, setting => setting.Value)
        };
    }

    private static void EnsureConfigFile()
    {
        string configPath = GetConfigPath();
        string? dataFolder = Path.GetDirectoryName(configPath);

        if (!string.IsNullOrWhiteSpace(dataFolder) && !Directory.Exists(dataFolder))
        {
            Directory.CreateDirectory(dataFolder);
        }

        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, "{\"Devices\":[]}");
        }
    }

    private static string ReadConfigJson()
    {
        return File.ReadAllText(GetConfigPath());
    }

    private static string GetConfigPath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "device_config.json");
    }
}
