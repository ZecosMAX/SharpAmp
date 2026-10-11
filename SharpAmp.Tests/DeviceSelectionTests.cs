using SharpAmp.Cli;
using SharpAmp.Core.Audio;

namespace SharpAmp.Tests;

public sealed class DeviceSelectionTests
{
    private static readonly AvailableDevices Devices = new(
        [new(AudioBackend.Mme, AudioDirection.Input, "0", "Microphone")],
        [new(AudioBackend.Wasapi, AudioDirection.Output, "speakers", "Speakers"),
         new(AudioBackend.Wasapi, AudioDirection.Output, "headphones", "Headphones")]);

    [Fact]
    public void ListsRemainingBackendsWhenOneFailsAndUsesSeparateIndices()
    {
        using var writer = new StringWriter();
        using var errors = new StringWriter();
        var calls = new List<(AudioBackend, AudioDirection)>();
        var selection = new DeviceSelection(TextReader.Null, writer, errors, true, (backend, direction) =>
        {
            calls.Add((backend, direction));
            if (backend == AudioBackend.Asio)
                throw new IOException("Driver unavailable");
            return [new(backend, direction, "device", $"{backend} {direction}")];
        });

        var available = selection.ListDevices();

        Assert.Equal(8, calls.Count);
        Assert.Equal(3, available.Inputs.Count);
        Assert.Equal(3, available.Outputs.Count);
        Assert.Contains("Inputs:" + Environment.NewLine + "  0: [Mme] Mme Input", writer.ToString());
        Assert.Contains("Outputs:" + Environment.NewLine + "  0: [Mme] Mme Output", writer.ToString());
        Assert.Contains("  2: [DirectSound] DirectSound Output", writer.ToString());
        Assert.Contains("Cannot enumerate Asio Input: Driver unavailable", errors.ToString());
        Assert.Contains("Cannot enumerate Asio Output: Driver unavailable", errors.ToString());
    }

    [Fact]
    public void ExplicitIndicesWorkWithRedirectedInputWithoutPrompts()
    {
        using var writer = new StringWriter();
        var selection = new DeviceSelection(TextReader.Null, writer, TextWriter.Null, true);
        var result = selection.Select(Devices, new RunOptions { InputIndex = 0, OutputIndex = 1 });
        Assert.Same(Devices.Inputs[0], result.Input);
        Assert.Same(Devices.Outputs[1], result.Output);
        Assert.Equal("", writer.ToString());
    }

    [Fact]
    public void PromptsOnlyForMissingSelection()
    {
        using var reader = new StringReader("1\n");
        using var writer = new StringWriter();
        var selection = new DeviceSelection(reader, writer, TextWriter.Null, false);
        var result = selection.Select(Devices, new RunOptions { InputIndex = 0 });
        Assert.Same(Devices.Outputs[1], result.Output);
        Assert.Equal("Output index [0..1]: ", writer.ToString());
    }

    [Fact]
    public void RequiresExplicitSelectionsWhenInputIsRedirected()
    {
        var selection = new DeviceSelection(TextReader.Null, TextWriter.Null, TextWriter.Null, true);
        var error = Assert.Throws<ArgumentException>(() => selection.Select(Devices, new RunOptions { InputIndex = 0 }));
        Assert.Contains("Specify --input and --output", error.Message);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 2)]
    public void RejectsIndicesOutsideDisplayedLists(int input, int output)
    {
        var selection = new DeviceSelection(TextReader.Null, TextWriter.Null, TextWriter.Null, true);
        Assert.Throws<ArgumentException>(() => selection.Select(Devices, new RunOptions { InputIndex = input, OutputIndex = output }));
    }

    [Fact]
    public void RejectsMissingDevicesBeforePrompting()
    {
        var selection = new DeviceSelection(TextReader.Null, TextWriter.Null, TextWriter.Null, true);
        Assert.Throws<InvalidOperationException>(() => selection.Select(new([], Devices.Outputs), new RunOptions()));
        Assert.Throws<InvalidOperationException>(() => selection.Select(new(Devices.Inputs, []), new RunOptions()));
    }

    [Fact]
    public void ReportsEndOfInputAndMalformedInteractiveSelection()
    {
        var ended = new DeviceSelection(TextReader.Null, TextWriter.Null, TextWriter.Null, false);
        Assert.Throws<InvalidOperationException>(() => ended.Select(Devices, new RunOptions()));
        using var reader = new StringReader("invalid\n");
        var malformed = new DeviceSelection(reader, TextWriter.Null, TextWriter.Null, false);
        var error = Assert.Throws<ArgumentException>(() => malformed.Select(Devices, new RunOptions()));
        Assert.Contains("Input index must be an integer", error.Message);
    }
}
