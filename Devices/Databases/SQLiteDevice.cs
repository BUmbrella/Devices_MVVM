using System.Data.Common;
using Devices_MVVM.Devices.Models;
using Microsoft.Data.Sqlite;

namespace Devices_MVVM.Devices.Databases;

public sealed class SQLiteDevice : DatabaseDeviceBase
{
    public SQLiteDevice(DeviceDefinition definition)
        : base(definition)
    {
    }

    protected override DbConnection CreateConnection()
    {
        string databasePath = GetRequiredSetting("DatabasePath");
        string fullPath = Path.IsPathRooted(databasePath)
            ? databasePath
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, databasePath);

        return new SqliteConnection($"Data Source={fullPath}");
    }
}
