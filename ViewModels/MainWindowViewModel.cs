using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;
using Devices_MVVM.Models;
using Devices_MVVM.Services;
using Devices_MVVM.Views;
using JYPPX.TensorRtSharp.Cuda;
using JYPPX.TensorRtSharp.Nvinfer;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;

namespace Devices_MVVM.ViewModels;

public sealed class MainWindowViewModel : BindableBase
{
    // TensorRT 是 native 库，日志回调会被 native 层保存并在构建过程中调用。
    // 这里必须用静态字段长期持有委托引用，避免局部委托被 GC 回收后 native 层再回调，导致 AccessViolationException。
    private static readonly LogCallbackFunction TensorRtBuildLogCallback =
        message => System.Diagnostics.Debug.WriteLine(message);

    private readonly DeviceBootstrapper _deviceBootstrapper;
    private readonly IDeviceLogService _deviceLogService;
    
    private string _statusText = "未初始化";
    private bool _isRunning;
    private bool _isParameterSettingsVisible;
    private bool _isConvertingOnnx;
    private string _selectedOnnxModelType = "YOLO";
    private string _selectedOnnxInputSize = "640x640";
    private int _onnxInputWidth = 640;
    private int _onnxInputHeight = 640;
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
        ConvertOnnxToEngineCommand = new DelegateCommand(ConvertOnnxToEngine, CanConvertOnnxToEngine);
        AddDeviceCommand = new DelegateCommand(AddDevice);
        DeleteDeviceCommand = new DelegateCommand<DeviceParameterItem>(DeleteDevice);
    }
    //ObservableCollection<string> 的作用是：集合内容变化时，会自动通知界面刷新。
    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<DeviceParameterItem> DeviceParameters { get; } = new();
    public ObservableCollection<DeviceParameterGroup> DeviceParameterGroups { get; } = new();
    public ObservableCollection<string> OnnxInputSizeOptions { get; } = new()
    {
        "640x640",
        "512x512",
        "416x416",
        "320x320"
    };
    public IReadOnlyList<string> OnnxModelTypes { get; } = ["YOLO", "ResNet", "UNet"];
    public DelegateCommand StartMainProcessCommand { get; }
    public DelegateCommand OpenParameterSettingsCommand { get; }
    public DelegateCommand ConfirmParameterSettingsCommand { get; }
    public DelegateCommand CancelParameterSettingsCommand { get; }
    public DelegateCommand ExitParameterSettingsCommand { get; }
    public DelegateCommand ConvertOnnxToEngineCommand { get; }
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

    public string SelectedOnnxModelType
    {
        get => _selectedOnnxModelType;
        set
        {
            if (SetProperty(ref _selectedOnnxModelType, value))
            {
                ApplyDefaultOnnxInputSize(value);
            }
        }
    }

    public int OnnxInputWidth
    {
        get => _onnxInputWidth;
        set => SetProperty(ref _onnxInputWidth, Math.Max(1, value));
    }

    public int OnnxInputHeight
    {
        get => _onnxInputHeight;
        set => SetProperty(ref _onnxInputHeight, Math.Max(1, value));
    }

    public string SelectedOnnxInputSize
    {
        get => _selectedOnnxInputSize;
        set
        {
            if (SetProperty(ref _selectedOnnxInputSize, value))
            {
                ApplySelectedOnnxInputSize(value);
            }
        }
    }

    private bool IsConvertingOnnx
    {
        get => _isConvertingOnnx;
        set
        {
            if (SetProperty(ref _isConvertingOnnx, value))
            {
                ConvertOnnxToEngineCommand.RaiseCanExecuteChanged();
            }
        }
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

    private bool CanConvertOnnxToEngine()
    {
        return !IsConvertingOnnx;
    }

    private void ApplyDefaultOnnxInputSize(string modelType)
    {
        OnnxInputSizeOptions.Clear();

        switch (modelType)
        {
            case "ResNet":
                AddOnnxInputSizes("224x224", "256x256", "384x384", "448x448");
                SelectedOnnxInputSize = "224x224";
                break;
            case "UNet":
                AddOnnxInputSizes("256x256", "512x512", "640x640", "1024x1024");
                SelectedOnnxInputSize = "256x256";
                break;
            default:
                AddOnnxInputSizes("640x640", "512x512", "416x416", "320x320");
                SelectedOnnxInputSize = "640x640";
                break;
        }
    }

    private void AddOnnxInputSizes(params string[] sizes)
    {
        foreach (string size in sizes)
        {
            OnnxInputSizeOptions.Add(size);
        }
    }

    private void ApplySelectedOnnxInputSize(string sizeText)
    {
        if (string.IsNullOrWhiteSpace(sizeText))
        {
            return;
        }

        string[] parts = sizeText.Split('x', 'X');
        if (parts.Length != 2)
        {
            return;
        }

        if (int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
        {
            OnnxInputWidth = width;
            OnnxInputHeight = height;
        }
    }

    private async void ConvertOnnxToEngine()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 ONNX 模型文件",
            Filter = "ONNX 模型 (*.onnx)|*.onnx|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
        {
            return;
        }

        string onnxPath = dialog.FileName;
        string enginePath = Path.ChangeExtension(onnxPath, ".engine");

        try
        {
            IsConvertingOnnx = true;
            StatusText = "正在转换 ONNX...";

            int inputWidth = OnnxInputWidth;
            int inputHeight = OnnxInputHeight;
            bool isResNet = string.Equals(SelectedOnnxModelType, "ResNet", StringComparison.OrdinalIgnoreCase);
            bool isUNet = string.Equals(SelectedOnnxModelType, "UNet", StringComparison.OrdinalIgnoreCase);
            bool enableFp16 = !isResNet && !isUNet;
            bool addOptimizationProfile = !isResNet && !isUNet;
            int minBatch = 1;
            int optBatch = isResNet ? 16 : 1;
            int maxBatch = isResNet ? 32 : 1;

            await Task.Run(() => BuildTensorRtEngine(
                onnxPath,
                enginePath,
                inputWidth,
                inputHeight,
                enableFp16,
                addOptimizationProfile,
                minBatch,
                optBatch,
                maxBatch));

            StatusText = "ONNX 转换完成";
            MessageBox.Show(
                Application.Current.MainWindow,
                $"转换成功：{enginePath}",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = "ONNX 转换失败";
            MessageBox.Show(
                Application.Current.MainWindow,
                $"转换失败: {ex.Message}",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsConvertingOnnx = false;
        }
    }

    private static void BuildTensorRtEngine(
        string onnxPath,
        string enginePath,
        int inputWidth,
        int inputHeight,
        bool enableFp16,
        bool addOptimizationProfile,
        int minBatch,
        int optBatch,
        int maxBatch)
    {
        // 配置 TensorRT 日志回调。这里输出到 Debug，避免依赖控制台窗口。
        Logger.Instance.SetCallback(TensorRtBuildLogCallback);
        Logger.Instance.SetThreshold(LoggerSeverity.kINFO);

        // 1. 创建 TensorRT Builder。Builder 是 ONNX -> Engine 的核心入口。
        Builder builder = new Builder();

        // 2. 创建网络定义。kEXPLICIT_BATCH 表示网络显式包含 batch 维度，是新版本 TensorRT 的常用方式。
        NetworkDefinition networkDefinition = builder.createNetworkV2(TrtNetworkDefinitionCreationFlag.kEXPLICIT_BATCH);

        // 3. 创建构建配置。这里可以设置 FP16、workspace 等构建参数。
        BuilderConfig builderConfig = builder.createBuilderConfig();

        // 4. 创建 ONNX 解析器，把 ONNX 模型解析到 networkDefinition 里。
        OnnxParser onnxParser = new OnnxParser(networkDefinition);

        // parseFromFile 第二个参数是日志级别：2 表示 WARNING 及以上。
        if (!onnxParser.parseFromFile(onnxPath, 2))
        {
            throw new InvalidOperationException("解析 ONNX 模型失败。");
        }

        // 动态 batch 的 ResNet 需要 optimization profile，例如 [1,3,224,224] 到 [32,3,224,224]。
        // 固定输入模型如果没有动态维度，这个函数会自动返回，不会额外添加 profile。
        if (addOptimizationProfile)
        {
            AddOptimizationProfileIfNeeded(
                builder,
                builderConfig,
                networkDefinition,
                inputWidth,
                inputHeight,
                minBatch,
                optBatch,
                maxBatch);
        }

        // 5. 可选启用 FP16 构建。YOLO/UNet 通常更吃算力，FP16 更有收益；
        // ResNet 分类模型先默认用 FP32，避免某些导出的 ResNet34 ONNX 在 native 构建阶段不稳定。
        if (enableFp16)
        {
            builderConfig.setFlag(TrtBuilderFlag.kFP16);
        }

        // 6. 设置构建 profile stream。TensorRT 构建时会做层性能分析和优化。
        CudaStream cudaStream = new CudaStream();
        builderConfig.setProfileStream(cudaStream);

        // 7. 构建并序列化网络。这个步骤可能耗时较长。
        HostMemory hostMemory = builder.buildSerializedNetwork(networkDefinition, builderConfig);

        // 8. 输出 engine 名称和输入 ONNX 保持一致，只把扩展名改成 .engine。
        using FileStream fs = new FileStream(enginePath, FileMode.Create, FileAccess.Write);
        fs.Write(hostMemory.getByteData(), 0, (int)hostMemory.Size);

        cudaStream.Dispose();
    }

    private static void AddOptimizationProfileIfNeeded(
        Builder builder,
        BuilderConfig builderConfig,
        NetworkDefinition networkDefinition,
        int inputWidth,
        int inputHeight,
        int minBatch,
        int optBatch,
        int maxBatch)
    {
        string inputName = "images";
        Dims minDims = new Dims(minBatch, 3, inputHeight, inputWidth);
        Dims optDims = new Dims(optBatch, 3, inputHeight, inputWidth);
        Dims maxDims = new Dims(maxBatch, 3, inputHeight, inputWidth);

        var profile = builder.createOptimizationProfile();
        profile.SetDimensions(inputName, TrtOptProfileSelector.kMIN, minDims);
        profile.SetDimensions(inputName, TrtOptProfileSelector.kOPT, optDims);
        profile.SetDimensions(inputName, TrtOptProfileSelector.kMAX, maxDims);
        builderConfig.addOptimizationProfile(profile);
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
