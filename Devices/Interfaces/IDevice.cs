using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Devices.Interfaces;

public interface IDevice
{
    string Id { get; }
    string Name { get; }
    DeviceType DeviceType { get; }
    DeviceStatus Status { get; }

    event EventHandler<DeviceStatusChangedEventArgs>? StatusChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
