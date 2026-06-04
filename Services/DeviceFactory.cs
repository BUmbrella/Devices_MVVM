using Devices_MVVM.Devices.Cameras;
using Devices_MVVM.Devices.Communication;
using Devices_MVVM.Devices.Databases;
using Devices_MVVM.Devices.Interfaces;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Services;

public sealed class DeviceFactory : IDeviceFactory
{
    public IDevice Create(DeviceDefinition def)
    {
        return def.Kind switch
        {
            "HikArea" => new HikAreaCameraDevice(def),
            "HikLine" => new HikLineScanCameraDevice(def),
            "ModbusTcp" => new ModbusTcpDevice(def),
            "SerialPort" => new SerialPortDevice(def),
            "SqlServer" => new SqlServerDevice(def),
            "SQLite" => new SQLiteDevice(def),
            _ => throw new NotSupportedException($"暂未实现设备类型 {def.Kind} 的创建设备逻辑")
        };
    }
}
