using System.Data.Common;
using Devices_MVVM.Devices.Models;
using MySqlConnector.MySqlClient;

namespace Devices_MVVM.Devices.Databases;

public sealed class MySqlDevice : DatabaseDeviceBase
{
    public MySqlDevice(DeviceDefinition definition)
        : base(definition)
    {
        ConnectionString = GetRequiredSetting("ConnectionString");
    }

    public string ConnectionString { get; }

    protected override DbConnection CreateConnection()
    {
        return new MySqlConnection(ConnectionString);
    }
}
