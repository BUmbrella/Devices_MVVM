using System.Collections.Concurrent;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Services;

public sealed class DeviceStateStore : IDeviceStateStore
{
    private readonly ConcurrentDictionary<string, DeviceRuntimeState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncRoot = new();

    public event EventHandler<DeviceRuntimeState>? StateChanged;

    public IReadOnlyCollection<DeviceRuntimeState> States => _states.Values.ToArray();

    public DeviceRuntimeState GetOrCreate(string deviceId, string? deviceName = null, DeviceType? deviceType = null)
    {
        return _states.GetOrAdd(
            deviceId,
            id => new DeviceRuntimeState
            {
                DeviceId = id,
                DeviceName = deviceName ?? id,
                DeviceType = deviceType ?? DeviceType.Unknown,
                Status = DeviceStatus.Created,
                LastUpdatedTime = DateTime.Now
            });
    }

    public bool TryGet(string deviceId, out DeviceRuntimeState? state)
    {
        var success = _states.TryGetValue(deviceId, out var current);
        state = current;
        return success;
    }

    public void Update(string deviceId, Action<DeviceRuntimeState> updateAction, string? deviceName = null, DeviceType? deviceType = null)
    {
        DeviceRuntimeState state;

        lock (_syncRoot)
        {
            state = GetOrCreate(deviceId, deviceName, deviceType);

            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                state.DeviceName = deviceName;
            }

            if (deviceType.HasValue)
            {
                state.DeviceType = deviceType.Value;
            }

            updateAction(state);
            state.LastUpdatedTime = DateTime.Now;
        }

        StateChanged?.Invoke(this, state);
    }
}
