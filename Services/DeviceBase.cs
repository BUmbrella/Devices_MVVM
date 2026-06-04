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

        throw new InvalidOperationException($"设备 {Name} 缺少配置项: {key}");
    }

    protected double GetDoubleSetting(string key)
    {
        if (double.TryParse(GetRequiredSetting(key), out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"设备 {Name} 的配置项 {key} 不是有效数字");
    }

    protected void SetStatus(DeviceStatus newStatus)
    {
        if (_status == newStatus)
        {
            return;
        }

        var oldStatus = _status;
        _status = newStatus;
        StatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs(oldStatus, newStatus));
    }
}
