using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;
using Devices_MVVM.Main;
using Devices_MVVM.Models;
using Devices_MVVM.Services;
using Devices_MVVM.Views;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;

namespace Devices_MVVM.ViewModels;

public sealed class MainWindowViewModel : BindableBase
{
    private readonly DeviceBootstrapper _deviceBootstrapper;
    private readonly IDeviceLogService _deviceLogService;
    private readonly MainProcessService _mainProcessService;
    
    private string _statusText = "未初始化";
    private bool _isRunning;
    private bool _isStopping;
    private bool _canRunMainProcess;
    private bool _isParameterSettingsVisible;
    private bool _isConvertingOnnx;
    private string _selectedOnnxModelType = "YOLO";
    private string _selectedOnnxInputSize = "640x640";
    private int _onnxInputWidth = 640;
    private int _onnxInputHeight = 640;
    private string _parameterSettingsSnapshot = string.Empty;
    private CancellationTokenSource? _mainProcessCts;
    private Task? _mainProcessTask;

    public MainWindowViewModel(
        DeviceBootstrapper deviceBootstrapper,
        IDeviceLogService deviceLogService,
        MainProcessService mainProcessService)
    {
        _deviceBootstrapper = deviceBootstrapper;
        _deviceLogService = deviceLogService;
        _mainProcessService = mainProcessService;

        
        //订阅所有设备触发日志
        _deviceLogService.LogAdded += OnLogAdded;
        _mainProcessService.StatusChanged += OnMainProcessStatusChanged;

        StartMainProcessCommand = new DelegateCommand(StartMainProcess, CanStartMainProcess);
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

    public string StartButtonText => IsRunning ? (_isStopping ? "停止中" : "运行中") : "启动";

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

            DeviceBootstrapper.DeviceBootstrapResult result = await _deviceBootstrapper.InitializeAsync();

            if (result.HasFailedDevice)
            {
                _canRunMainProcess = false;
                StatusText = "初始化失败";
                StartMainProcessCommand.RaiseCanExecuteChanged();

                string failedText = string.Join(
                    Environment.NewLine,
                    result.FailedDevices.Select(device =>
                        $"{device.DeviceName} ({device.DeviceId}, {device.Kind})：{device.ErrorMessage}"));

                MessageBox.Show(
                    Application.Current.MainWindow,
                    $"以下设备初始化失败，启动按钮已禁用：{Environment.NewLine}{Environment.NewLine}{failedText}",
                    "设备初始化失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            _canRunMainProcess = true;
            StatusText = "初始化完成";
            StartMainProcessCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _canRunMainProcess = false;
            StatusText = "初始化失败";
            StartMainProcessCommand.RaiseCanExecuteChanged();
            _deviceLogService.Info("System", $"设备初始化失败，异常: {ex}");

            MessageBox.Show(
                Application.Current.MainWindow,
                $"设备初始化异常，启动按钮已禁用：{ex.Message}",
                "设备初始化失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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

    private bool CanStartMainProcess()
    {
        return _canRunMainProcess && !_isStopping;
    }

    private async void StartMainProcess()
    {
        if (_isStopping)
        {
            return;
        }

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

            await StopMainProcessAsync();
            return;
        }

        StartMainProcessCore();
    }

    private void StartMainProcessCore()
    {
        _mainProcessCts?.Dispose();
        _mainProcessCts = new CancellationTokenSource();

        IsRunning = true;
        StatusText = "运行中";
        StartMainProcessCommand.RaiseCanExecuteChanged();

        _mainProcessTask = RunMainProcessAndObserveAsync(_mainProcessCts.Token);
    }

    private async Task RunMainProcessAndObserveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _mainProcessService.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 正常停止路径，不需要弹异常。
        }
        catch (Exception ex)
        {
            _deviceLogService.Info("Main", $"主流程任务异常: {ex}");

            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"主流程异常: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        finally
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _isStopping = false;
                IsRunning = false;
                StatusText = "已停止";
                StartMainProcessCommand.RaiseCanExecuteChanged();
            });
        }
    }

    private async Task StopMainProcessAsync()
    {
        if (_mainProcessCts == null)
        {
            IsRunning = false;
            StatusText = "已停止";
            return;
        }

        _isStopping = true;
        StatusText = "正在停止...";
        RaisePropertyChanged(nameof(StartButtonText));
        StartMainProcessCommand.RaiseCanExecuteChanged();

        try
        {
            _mainProcessCts.Cancel();

            if (_mainProcessTask != null)
            {
                await _mainProcessTask;
            }
        }
        finally
        {
            _mainProcessCts.Dispose();
            _mainProcessCts = null;
            _mainProcessTask = null;
            _isStopping = false;
            IsRunning = false;
            StatusText = "已停止";
            StartMainProcessCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnMainProcessStatusChanged(object? sender, string status)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            StatusText = status;
            return;
        }

        dispatcher.Invoke(() => StatusText = status);
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
        string enginePath = GetEnginePath(onnxPath);
        string? trtexecPath = ResolveTrtexecPath();

        if (string.IsNullOrWhiteSpace(trtexecPath))
        {
            MessageBox.Show(
                Application.Current.MainWindow,
                "没有找到 trtexec.exe。请安装 NVIDIA TensorRT，或在弹出的文件选择框中选择 trtexec.exe。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsConvertingOnnx = true;
            StatusText = "正在转换 ONNX...";

            int inputWidth = OnnxInputWidth;
            int inputHeight = OnnxInputHeight;
            bool isResNet = string.Equals(SelectedOnnxModelType, "ResNet", StringComparison.OrdinalIgnoreCase);
            bool enableFp16 = !isResNet;
            int minBatch = 1;
            int optBatch = 8;
            int maxBatch = 32;

            bool usedStaticFallback = await Task.Run(() => BuildTensorRtEngineWithTrtexec(
                trtexecPath,
                onnxPath,
                enginePath,
                inputWidth,
                inputHeight,
                minBatch,
                optBatch,
                maxBatch,
                enableFp16));

            StatusText = "ONNX 转换完成";
            string message = $"转换成功：{enginePath}";
            if (usedStaticFallback && string.Equals(SelectedOnnxModelType, "ResNet", StringComparison.OrdinalIgnoreCase))
            {
                message += $"{Environment.NewLine}{Environment.NewLine}注意：当前 ResNet ONNX 是静态输入模型，本次生成的是静态 engine，不支持 1~32 动态 batch。";
            }

            MessageBox.Show(
                Application.Current.MainWindow,
                message,
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

    private string GetEnginePath(string onnxPath)
    {
        string? folder = Path.GetDirectoryName(onnxPath);
        string fileName = Path.GetFileNameWithoutExtension(onnxPath);

        if (string.Equals(SelectedOnnxModelType, "ResNet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SelectedOnnxModelType, "UNet", StringComparison.OrdinalIgnoreCase))
        {
            if (!fileName.EndsWith("_dynamic", StringComparison.OrdinalIgnoreCase))
            {
                fileName += "_dynamic";
            }
        }

        return Path.Combine(folder ?? string.Empty, fileName + ".engine");
    }

    private string? ResolveTrtexecPath()
    {
        string? pathTrtexec = FindExecutableOnPath("trtexec.exe");
        if (!string.IsNullOrWhiteSpace(pathTrtexec))
        {
            return pathTrtexec;
        }

        string? tensorRtHome = Environment.GetEnvironmentVariable("TENSORRT_HOME");
        if (!string.IsNullOrWhiteSpace(tensorRtHome))
        {
            string candidate = Path.Combine(tensorRtHome, "bin", "trtexec.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string localCandidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trtexec.exe");
        if (File.Exists(localCandidate))
        {
            return localCandidate;
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择 trtexec.exe",
            Filter = "trtexec.exe|trtexec.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(Application.Current.MainWindow) == true
            ? dialog.FileName
            : null;
    }

    private static string? FindExecutableOnPath(string fileName)
    {
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (string folder in pathValue.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            string candidate = Path.Combine(folder.Trim(), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool BuildTensorRtEngineWithTrtexec(
        string trtexecPath,
        string onnxPath,
        string enginePath,
        int inputWidth,
        int inputHeight,
        int minBatch,
        int optBatch,
        int maxBatch,
        bool enableFp16)
    {
        TrtexecResult result = RunTrtexec(
            trtexecPath,
            onnxPath,
            enginePath,
            inputWidth,
            inputHeight,
            minBatch,
            optBatch,
            maxBatch,
            enableFp16,
            useDynamicShapes: true);

        if (result.ExitCode == 0)
        {
            return false;
        }

        string message = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        if (message.Contains("Static model does not take explicit shapes", StringComparison.OrdinalIgnoreCase))
        {
            TrtexecResult staticResult = RunTrtexec(
                trtexecPath,
                onnxPath,
                enginePath,
                inputWidth,
                inputHeight,
                minBatch,
                optBatch,
                maxBatch,
                enableFp16,
                useDynamicShapes: false);

            if (staticResult.ExitCode == 0)
            {
                return true;
            }

            string staticMessage = string.IsNullOrWhiteSpace(staticResult.Error) ? staticResult.Output : staticResult.Error;
            throw new InvalidOperationException($"trtexec 转换失败，退出码 {staticResult.ExitCode}。{Environment.NewLine}{staticMessage}");
        }

        throw new InvalidOperationException($"trtexec 转换失败，退出码 {result.ExitCode}。{Environment.NewLine}{message}");
    }

    private static TrtexecResult RunTrtexec(
        string trtexecPath,
        string onnxPath,
        string enginePath,
        int inputWidth,
        int inputHeight,
        int minBatch,
        int optBatch,
        int maxBatch,
        bool enableFp16,
        bool useDynamicShapes)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = trtexecPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add($"--onnx={onnxPath}");
        startInfo.ArgumentList.Add($"--saveEngine={enginePath}");

        if (useDynamicShapes)
        {
            string minShape = $"images:{minBatch}x3x{inputHeight}x{inputWidth}";
            string optShape = $"images:{optBatch}x3x{inputHeight}x{inputWidth}";
            string maxShape = $"images:{maxBatch}x3x{inputHeight}x{inputWidth}";

            startInfo.ArgumentList.Add($"--minShapes={minShape}");
            startInfo.ArgumentList.Add($"--optShapes={optShape}");
            startInfo.ArgumentList.Add($"--maxShapes={maxShape}");
        }

        if (enableFp16)
        {
            startInfo.ArgumentList.Add("--fp16");
        }

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("启动 trtexec 失败。");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new TrtexecResult(process.ExitCode, output, error);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                "没有找到 trtexec。请安装 NVIDIA TensorRT，并把包含 trtexec.exe 的目录加入系统 PATH。",
                ex);
        }
    }

    private sealed record TrtexecResult(int ExitCode, string Output, string Error);

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
