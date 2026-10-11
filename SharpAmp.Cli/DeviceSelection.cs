using System.Globalization;
using SharpAmp.Core.Audio;

namespace SharpAmp.Cli;

internal sealed record AvailableDevices(IReadOnlyList<AudioDevice> Inputs, IReadOnlyList<AudioDevice> Outputs);

internal sealed class DeviceSelection(
    TextReader reader,
    TextWriter writer,
    TextWriter errorWriter,
    bool isInputRedirected,
    Func<AudioBackend, AudioDirection, IReadOnlyList<AudioDevice>>? enumerateDevices = null)
{
    private readonly Func<AudioBackend, AudioDirection, IReadOnlyList<AudioDevice>> enumerate =
        enumerateDevices ?? AudioDeviceCatalog.GetDevices;

    public AvailableDevices ListDevices()
    {
        var devices = new AvailableDevices(Enumerate(AudioDirection.Input), Enumerate(AudioDirection.Output));
        PrintDevices("Inputs", devices.Inputs);
        PrintDevices("Outputs", devices.Outputs);
        return devices;
    }

    public AudioSessionOptions Select(AvailableDevices devices, RunOptions options)
    {
        if (devices.Inputs.Count == 0 || devices.Outputs.Count == 0)
            throw new InvalidOperationException("At least one input and output device are required.");

        var inputIndex = options.InputIndex ?? Prompt("Input", devices.Inputs.Count);
        var outputIndex = options.OutputIndex ?? Prompt("Output", devices.Outputs.Count);
        if (inputIndex < 0 || inputIndex >= devices.Inputs.Count || outputIndex < 0 || outputIndex >= devices.Outputs.Count)
            throw new ArgumentException("Device index is outside the displayed list.");

        return options.CreateSessionOptions(devices.Inputs[inputIndex], devices.Outputs[outputIndex]);
    }

    private List<AudioDevice> Enumerate(AudioDirection direction)
    {
        var devices = new List<AudioDevice>();
        foreach (var backend in Enum.GetValues<AudioBackend>())
        {
            try
            {
                devices.AddRange(enumerate(backend, direction));
            }
            catch (Exception error)
            {
                errorWriter.WriteLine($"Cannot enumerate {backend} {direction}: {error.Message}");
            }
        }
        return devices;
    }

    private void PrintDevices(string heading, IReadOnlyList<AudioDevice> devices)
    {
        writer.WriteLine(heading + ":");
        for (var i = 0; i < devices.Count; i++)
            writer.WriteLine($"  {i}: [{devices[i].Backend}] {devices[i].Name}");
    }

    private int Prompt(string label, int count)
    {
        if (isInputRedirected)
            throw new ArgumentException("Specify --input and --output when standard input is redirected.");

        writer.Write($"{label} index [0..{count - 1}]: ");
        var value = reader.ReadLine() ?? throw new InvalidOperationException("Input ended.");
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            throw new ArgumentException($"{label} index must be an integer.");
        return index;
    }
}
