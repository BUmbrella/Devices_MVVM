namespace Devices_MVVM.Devices.Interfaces;

public interface IDeviceManager
{
    IReadOnlyCollection<IDevice> Devices { get; }

    bool Register(IDevice device);
    IDevice Get(string deviceId);

    Task InitializeAsync(string deviceId, CancellationToken cancellationToken = default);
    Task StartAsync(string deviceId, CancellationToken cancellationToken = default);
    Task StopAsync(string deviceId, CancellationToken cancellationToken = default);

    
}
