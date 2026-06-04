using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Devices.Interfaces;

public interface IDeviceStateStore
{
    event EventHandler<DeviceRuntimeState>? StateChanged;

    IReadOnlyCollection<DeviceRuntimeState> States { get; }

    DeviceRuntimeState GetOrCreate(string deviceId, string? deviceName = null, DeviceType? deviceType = null);
    bool TryGet(string deviceId, out DeviceRuntimeState? state);
    void Update(string deviceId, Action<DeviceRuntimeState> updateAction, string? deviceName = null, DeviceType? deviceType = null);
}
