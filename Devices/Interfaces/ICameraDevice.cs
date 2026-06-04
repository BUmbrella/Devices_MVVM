namespace Devices_MVVM.Devices.Interfaces;

public interface ICameraDevice : IDevice
{
    Task TakePhotoAsync(CancellationToken cancellationToken = default);
}
