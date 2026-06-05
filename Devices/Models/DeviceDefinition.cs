namespace Devices_MVVM.Devices.Models;

public sealed class DeviceDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DeviceType DeviceType { get; set; } = DeviceType.Unknown;

    // 用来区分同一大类下面的具体设备，例如 HikArea、HikLine、ModbusTcp。
    public string Kind { get; set; } = string.Empty;

    // 设置为 true 时，加载配置但跳过创建设备对象。
    public bool SkipCreate { get; set; }

    public Dictionary<string, string> Settings { get; set; } = new();
}
