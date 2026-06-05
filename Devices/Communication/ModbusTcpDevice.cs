using System.Net.Sockets;
using Devices_MVVM.Devices.Models;
using Devices_MVVM.Services;
using NModbus;
using NModbus.Device;

namespace Devices_MVVM.Devices.Communication;

public sealed class ModbusTcpDevice : DeviceBase, IDisposable
{
    private const int TimeoutMs = 500;
    private const int RetryCount = 2;
    private const int ReconnectFailCount = 3;
    private const int ReconnectDelayMs = 2000;

    private readonly object _syncLock = new();
    private TcpClient? _client;
    private IModbusMaster? _master;
    private int _failCount;
    private bool _isReconnecting;
    private DateTime _lastReconnectTime = DateTime.MinValue;

    public ModbusTcpDevice(DeviceDefinition definition)
        : base(definition)
    {
        IpAddress = GetRequiredSetting("IpAddress");
        Port = GetIntSetting("Port");
        SlaveId = (byte)GetOptionalIntSetting("SlaveId", 1);
        TestAddress = (ushort)GetOptionalIntSetting("TestAddress", 0);
    }

    public string IpAddress { get; }
    public int Port { get; }
    public byte SlaveId { get; }
    public ushort TestAddress { get; }

    public override Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetStatus(DeviceStatus.Initializing);

        Connect();
        ReadRegister(TestAddress);

        SetStatus(DeviceStatus.Ready);
        return Task.CompletedTask;
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureConnected();
        SetStatus(DeviceStatus.Running);
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Close();
        SetStatus(DeviceStatus.Stopped);
        return Task.CompletedTask;
    }

    public ushort ReadRegister(ushort address)
    {
        lock (_syncLock)
        {
            for (int i = 0; i < RetryCount; i++)
            {
                try
                {
                    EnsureConnected();
                    ushort[] result = _master!.ReadHoldingRegisters(SlaveId, address, 1);
                    _failCount = 0;
                    return result[0];
                }
                catch (Exception ex)
                {
                    HandleError(ex);
                }
            }

            throw new InvalidOperationException($"设备 {Name} 读取寄存器 D{address} 失败");
        }
    }

    public ushort[] ReadRegisters(ushort address, ushort count)
    {
        lock (_syncLock)
        {
            for (int i = 0; i < RetryCount; i++)
            {
                try
                {
                    EnsureConnected();
                    ushort[] result = _master!.ReadHoldingRegisters(SlaveId, address, count);
                    _failCount = 0;
                    return result;
                }
                catch (Exception ex)
                {
                    HandleError(ex);
                }
            }

            throw new InvalidOperationException($"设备 {Name} 读取寄存器 D{address} 起 {count} 个失败");
        }
    }

    public void WriteRegister(ushort address, ushort value)
    {
        lock (_syncLock)
        {
            for (int i = 0; i < RetryCount; i++)
            {
                try
                {
                    EnsureConnected();
                    _master!.WriteSingleRegister(SlaveId, address, value);
                    _failCount = 0;
                    return;
                }
                catch (Exception ex)
                {
                    HandleError(ex);
                }
            }

            throw new InvalidOperationException($"设备 {Name} 写入寄存器 D{address} 失败");
        }
    }

    public void WriteRegisters(ushort address, ushort[] values)
    {
        lock (_syncLock)
        {
            for (int i = 0; i < RetryCount; i++)
            {
                try
                {
                    EnsureConnected();
                    _master!.WriteMultipleRegisters(SlaveId, address, values);
                    _failCount = 0;
                    return;
                }
                catch (Exception ex)
                {
                    HandleError(ex);
                }
            }

            throw new InvalidOperationException($"设备 {Name} 批量写入寄存器 D{address} 失败");
        }
    }

    private bool Connect()
    {
        lock (_syncLock)
        {
            if (_isReconnecting)
            {
                return false;
            }

            if ((DateTime.Now - _lastReconnectTime).TotalMilliseconds < ReconnectDelayMs)
            {
                return _master != null;
            }

            _isReconnecting = true;
            _lastReconnectTime = DateTime.Now;

            try
            {
                SafeClose();

                _client = new TcpClient
                {
                    NoDelay = true,
                    ReceiveTimeout = TimeoutMs,
                    SendTimeout = TimeoutMs
                };

                IAsyncResult result = _client.BeginConnect(IpAddress, Port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(TimeoutMs);
                if (!success)
                {
                    throw new TimeoutException($"连接 {IpAddress}:{Port} 超时");
                }

                _client.EndConnect(result);

                _master = new ModbusFactory().CreateMaster(_client);
                _master.Transport.ReadTimeout = TimeoutMs;
                _master.Transport.WriteTimeout = TimeoutMs;
                _master.Transport.Retries = 0;

                _failCount = 0;
                SetStatus(DeviceStatus.Ready);
                return true;
            }
            catch
            {
                _master = null;
                SafeClose();
                SetStatus(DeviceStatus.Disconnected);
                return false;
            }
            finally
            {
                _isReconnecting = false;
            }
        }
    }

    private void EnsureConnected()
    {
        if (_master != null)
        {
            return;
        }

        if (!Connect() || _master == null)
        {
            throw new InvalidOperationException($"设备 {Name} 重连失败");
        }
    }

    private void HandleError(Exception ex)
    {
        _failCount++;

        if (_failCount >= ReconnectFailCount)
        {
            _master = null;
            SafeClose();
            SetStatus(DeviceStatus.Disconnected);
        }

        Thread.Sleep(100);
    }

    private void SafeClose()
    {
        try
        {
            _client?.Client?.Shutdown(SocketShutdown.Both);
        }
        catch
        {
        }

        _client?.Close();
        _client?.Dispose();
        _client = null;
    }

    public void Close()
    {
        lock (_syncLock)
        {
            _master = null;
            SafeClose();
        }
    }

    public void Dispose()
    {
        Close();
    }
}
