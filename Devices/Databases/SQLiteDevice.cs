using System.Data.Common;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Devices.Databases;

public sealed class SQLiteDevice : DatabaseDeviceBase
{
    public SQLiteDevice(DeviceDefinition definition)
        : base(definition)
    {
    }

    protected override DbConnection CreateConnection()
    {
        throw new NotSupportedException("SQLiteDevice 尚未接入 SQLite 驱动，请添加 Microsoft.Data.Sqlite 后再实现 CreateConnection。");
    }
}
