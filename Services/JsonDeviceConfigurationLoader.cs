using System.IO;
using System.Text.Json;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Services;

public sealed class JsonDeviceConfigurationLoader : IDeviceConfigurationLoader
{
    private readonly string _configPath;

    public JsonDeviceConfigurationLoader(string configPath)
    {
        _configPath = configPath;
    }

    public IReadOnlyList<DeviceDefinition> Load()
    {
        EnsureSampleFile();

        var json = File.ReadAllText(_configPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var configuration = JsonSerializer.Deserialize<DeviceConfiguration>(json, options);
        return configuration?.Devices ?? [];
    }

    private void EnsureSampleFile()
    {
        var folder = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        if (File.Exists(_configPath))
        {
            return;
        }

        var sample = new DeviceConfiguration
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
                }
            ]
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        File.WriteAllText(_configPath, JsonSerializer.Serialize(sample, options));
    }
}
