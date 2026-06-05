using System.Data.Common;
using Devices_MVVM.Devices.Models;
using Microsoft.Data.SqlClient;

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
        return new SqlConnection(ConnectionString);
    }
}
