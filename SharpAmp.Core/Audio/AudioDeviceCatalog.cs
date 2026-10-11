using System.Globalization;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SharpAmp.Core.Audio;

public static class AudioDeviceCatalog
{
    /// <summary>Enumerate one backend independently, so a broken driver need not hide other backends.</summary>
    public static IReadOnlyList<AudioDevice> GetDevices(AudioBackend backend, AudioDirection direction)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        var devices = new List<AudioDevice>();
        switch (backend)
        {
            case AudioBackend.Mme:
                var count = direction == AudioDirection.Input ? WaveIn.DeviceCount : WaveOut.DeviceCount;
                for (var i = 0; i < count; i++)
                    devices.Add(new(backend, direction, i.ToString(CultureInfo.InvariantCulture),
                        direction == AudioDirection.Input ? WaveIn.GetCapabilities(i).ProductName : WaveOut.GetCapabilities(i).ProductName));
                break;
            case AudioBackend.Wasapi:
                using (var enumerator = new MMDeviceEnumerator())
                    foreach (var device in enumerator.EnumerateAudioEndPoints(direction == AudioDirection.Input ? DataFlow.Capture : DataFlow.Render, DeviceState.Active))
                        using (device) devices.Add(new(backend, direction, device.ID, device.FriendlyName));
                break;
            case AudioBackend.Asio:
                // Do not open every driver just to enumerate: some drivers are exclusive or display a panel.
                devices.AddRange(AsioOut.GetDriverNames().Select(name => new AudioDevice(backend, direction, name, name)));
                break;
            case AudioBackend.DirectSound:
                if (direction == AudioDirection.Input) return DirectSoundCapture.GetDevices();
                devices.AddRange(DirectSoundOut.Devices.Select(d => new AudioDevice(backend, direction, d.Guid.ToString(), d.Description)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(backend));
        }
        return devices;
    }
}
