using System.Collections.ObjectModel;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Models;

public sealed class DeviceParameterGroup
{
    public DeviceType DeviceType { get; set; } = DeviceType.Unknown;
    public string Title { get; set; } = string.Empty;
    public ObservableCollection<DeviceParameterItem> Devices { get; } = new();
}
