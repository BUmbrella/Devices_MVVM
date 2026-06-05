using Devices_MVVM.Devices.Models;
using Devices_MVVM.Services;

namespace Devices_MVVM.Devices.Databases;

public sealed class SqlServerDevice : DeviceBase
{
    public SqlServerDevice(DeviceDefinition definition)
        : base(definition)
    {
        ConnectionString = GetRequiredSetting("ConnectionString");
    }

    public string ConnectionString { get; }

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        SetStatus(DeviceStatus.Initializing);
        await Task.Delay(30, cancellationToken);
        SetStatus(DeviceStatus.Ready);
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(30, cancellationToken);
        SetStatus(DeviceStatus.Running);
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(30, cancellationToken);
        SetStatus(DeviceStatus.Stopped);
    }
}
