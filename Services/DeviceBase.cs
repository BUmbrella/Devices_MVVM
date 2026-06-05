using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Services;

public abstract class DeviceBase : IDevice
{
    private DeviceStatus _status = DeviceStatus.Created;

    protected DeviceBase(DeviceDefinition definition)
    {
        Definition = definition;
        Id = definition.Id;
        Name = definition.Name;
        DeviceType = definition.DeviceType;
    }

    protected DeviceDefinition Definition { get; }

    public string Id { get; }
    public string Name { get; }
    public DeviceType DeviceType { get; }
    public DeviceStatus Status => _status;

    public event EventHandler<DeviceStatusChangedEventArgs>? StatusChanged;

    public abstract Task InitializeAsync(CancellationToken cancellationToken = default);
    public abstract Task StartAsync(CancellationToken cancellationToken = default);
    public abstract Task StopAsync(CancellationToken cancellationToken = default);

    protected string GetRequiredSetting(string key)
    {
        if (Definition.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"设备 {Name} 缺少必要配置: {key}");
    }

    protected string GetOptionalSetting(string key, string defaultValue)
    {
        return Definition.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    protected double GetDoubleSetting(string key)
    {
        string value = GetRequiredSetting(key);

        if (double.TryParse(value, out var result))
        {
            return result;
        }

        throw new InvalidOperationException($"设备 {Name} 的配置 {key} 不是有效数字: {value}");
    }

    protected int GetIntSetting(string key)
    {
        string value = GetRequiredSetting(key);

        if (int.TryParse(value, out var result))
        {
            return result;
        }

        throw new InvalidOperationException($"设备 {Name} 的配置 {key} 不是有效整数: {value}");
    }

    protected int GetOptionalIntSetting(string key, int defaultValue)
    {
        string value = GetOptionalSetting(key, defaultValue.ToString());

        if (int.TryParse(value, out var result))
        {
            return result;
        }

        throw new InvalidOperationException($"设备 {Name} 的配置 {key} 不是有效整数: {value}");
    }

    protected void SetStatus(DeviceStatus status)
    {
        if (_status == status)
        {
            return;
        }

        var oldStatus = _status;
        _status = status;
        StatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs(oldStatus, status));
    }
}
