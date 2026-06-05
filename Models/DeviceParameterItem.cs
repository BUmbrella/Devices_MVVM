using System.Collections.ObjectModel;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Models;

public sealed class DeviceParameterItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DeviceType DeviceType { get; set; } = DeviceType.Unknown;
    public string Kind { get; set; } = string.Empty;
    public bool SkipCreate { get; set; }
    public double CardWidth => DeviceType == DeviceType.Database ? 880 : 430;
    public ObservableCollection<DeviceSettingItem> Settings { get; } = new();
}
