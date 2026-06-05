using System.Data.Common;
using Devices_MVVM.Devices.Models;
using Devices_MVVM.Services;

namespace Devices_MVVM.Devices.Databases;

public abstract class DatabaseDeviceBase : DeviceBase
{
    private const int RetryCount = 2;

    protected DatabaseDeviceBase(DeviceDefinition definition)
        : base(definition)
    {
    }

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        SetStatus(DeviceStatus.Initializing);

        if (!await PingAsync(cancellationToken))
        {
            SetStatus(DeviceStatus.Disconnected);
            throw new InvalidOperationException($"设备 {Name} 数据库连接测试失败");
        }

        SetStatus(DeviceStatus.Ready);
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        SetStatus(DeviceStatus.Running);
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        SetStatus(DeviceStatus.Stopped);
        return Task.CompletedTask;
    }

    public Task<List<Dictionary<string, object?>>> QueryAsync(
        string sql,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteQueryListAsync(sql, parameters, cancellationToken);
    }

    public Task<Dictionary<string, object?>?> QuerySingleAsync(
        string sql,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteQuerySingleAsync(sql, parameters, cancellationToken);
    }

    public Task<int> InsertAsync(
        string sql,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteNonQueryAsync(sql, parameters, cancellationToken);
    }

    public Task<int> UpdateAsync(
        string sql,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteNonQueryAsync(sql, parameters, cancellationToken);
    }

    public Task<int> DeleteAsync(
        string sql,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteNonQueryAsync(sql, parameters, cancellationToken);
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            object? result = await ExecuteScalarAsync("SELECT 1", null, cancellationToken);
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    protected abstract DbConnection CreateConnection();

    private async Task<List<Dictionary<string, object?>>> ExecuteQueryListAsync(
        string sql,
        Dictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        return await ExecuteWithReconnectAsync(async connection =>
        {
            await using DbCommand command = CreateCommand(connection, sql, parameters);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(ReadRow(reader));
            }

            return rows;
        }, cancellationToken);
    }

    private async Task<Dictionary<string, object?>?> ExecuteQuerySingleAsync(
        string sql,
        Dictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        List<Dictionary<string, object?>> rows = await ExecuteQueryListAsync(sql, parameters, cancellationToken);
        return rows.Count > 0 ? rows[0] : null;
    }

    private Task<int> ExecuteNonQueryAsync(
        string sql,
        Dictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteWithReconnectAsync(async connection =>
        {
            await using DbCommand command = CreateCommand(connection, sql, parameters);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    private Task<object?> ExecuteScalarAsync(
        string sql,
        Dictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteWithReconnectAsync(async connection =>
        {
            await using DbCommand command = CreateCommand(connection, sql, parameters);
            return await command.ExecuteScalarAsync(cancellationToken);
        }, cancellationToken);
    }

    private async Task<T> ExecuteWithReconnectAsync<T>(
        Func<DbConnection, Task<T>> action,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (int i = 0; i < RetryCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using DbConnection connection = CreateConnection();
            try
            {
                await connection.OpenAsync(cancellationToken);
                T result = await action(connection);
                SetStatus(DeviceStatus.Running);
                return result;
            }
            catch (Exception ex)
            {
                lastException = ex;
                SetStatus(DeviceStatus.Disconnected);

                if (i == RetryCount - 1)
                {
                    break;
                }

                await Task.Delay(500, cancellationToken);
            }
        }

        SetStatus(DeviceStatus.Faulted);
        throw lastException ?? new InvalidOperationException($"设备 {Name} 数据库操作失败");
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        string sql,
        Dictionary<string, object?>? parameters)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 10;

        if (parameters == null)
        {
            return command;
        }

        foreach (var item in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = item.Key;
            parameter.Value = item.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static Dictionary<string, object?> ReadRow(DbDataReader reader)
    {
        var row = new Dictionary<string, object?>();

        for (int i = 0; i < reader.FieldCount; i++)
        {
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return row;
    }
}
