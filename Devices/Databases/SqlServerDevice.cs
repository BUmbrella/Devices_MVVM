using System.Data.Common;
using Devices_MVVM.Devices.Models;

namespace Devices_MVVM.Devices.Databases;

public sealed class SqlServerDevice : DatabaseDeviceBase
{
    public SqlServerDevice(DeviceDefinition definition)
        : base(definition)
    {
        ConnectionString = GetRequiredSetting("ConnectionString");
    }

    public string ConnectionString { get; }

    protected override DbConnection CreateConnection()
    {
        throw new NotSupportedException("SqlServerDevice 尚未接入 SQL Server 驱动，请添加 Microsoft.Data.SqlClient 后再实现 CreateConnection。");
    }
}
