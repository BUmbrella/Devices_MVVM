using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Models;

public sealed class DeviceTemplateOption
{
    public string DisplayName { get; init; } = string.Empty;
    public DeviceType DeviceType { get; init; } = DeviceType.Unknown;
    public string Kind { get; init; } = string.Empty;
    public string IdPrefix { get; init; } = string.Empty;
    public Dictionary<string, string> Settings { get; init; } = new();
}
