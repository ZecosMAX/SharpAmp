using System.Globalization;
using SharpAmp.Cli;
using SharpAmp.Core.Audio;

namespace SharpAmp.Tests;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("")]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void RecognizesHelp(string arguments)
    {
        var command = CommandLine.Parse(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(CommandKind.Help, command.Kind);
    }

    [Fact]
    public void RunDefaultsPreserveInteractiveSelectionAndCurrentEffectSettings()
    {
        var command = CommandLine.Parse(["run"]);
        Assert.Equal(CommandKind.Run, command.Kind);
        Assert.Null(command.Options.InputIndex);
        Assert.Null(command.Options.OutputIndex);
        Assert.Equal(48000, command.Options.SampleRate);
        Assert.Equal(7, command.Options.Drive);
        Assert.Equal(0.2f, command.Options.Wet);
        Assert.Equal(0, command.Options.Seconds);

        var chain = command.Options.CreateProcessorChain();
        chain.Initialize(48000, 1);
        float[] samples = [0.1f];
        chain.Process(samples);
        Assert.Equal(0.196f, samples[0], 6);
    }

    [Fact]
    public void BindsAllOptionsAndMapsAudioSettingsToSession()
    {
        var command = CommandLine.Parse([
            "run", "--input", "2", "--output", "3", "--rate", "44100",
            "--input-channels", "2", "--output-channels", "4", "--input-offset", "1",
            "--output-offset", "2", "--latency", "25", "--buffer", "150",
            "--drive", "4.5", "--wet", "0.3", "--seconds", "10"]);
        var options = command.Options;
        Assert.Equal(2, options.InputIndex);
        Assert.Equal(3, options.OutputIndex);
        Assert.Equal(4.5f, options.Drive);
        Assert.Equal(0.3f, options.Wet);
        Assert.Equal(10, options.Seconds);
        Assert.False(options.Bypass);

        var input = new AudioDevice(AudioBackend.Asio, AudioDirection.Input, "driver", "Input");
        var output = new AudioDevice(AudioBackend.Asio, AudioDirection.Output, "driver", "Output");
        var session = options.CreateSessionOptions(input, output);
        Assert.Same(input, session.Input);
        Assert.Same(output, session.Output);
        Assert.Equal(44100, session.SampleRate);
        Assert.Equal(2, session.InputChannels);
        Assert.Equal(4, session.OutputChannels);
        Assert.Equal(1, session.AsioInputChannelOffset);
        Assert.Equal(2, session.AsioOutputChannelOffset);
        Assert.Equal(25, session.LatencyMilliseconds);
        Assert.Equal(150, session.BufferMilliseconds);
    }

    [Fact]
    public void ParsesNumbersUsingInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var command = CommandLine.Parse(["run", "--drive", "2.5", "--wet", "0.125"]);
            Assert.Equal(2.5f, command.Options.Drive);
            Assert.Equal(0.125f, command.Options.Wet);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void BypassDoesNotEvaluateUnusedEffectValues()
    {
        var command = CommandLine.Parse(["run", "--bypass", "--drive", "unused", "--wet", "unused"]);
        Assert.True(command.Options.Bypass);
        var chain = command.Options.CreateProcessorChain();
        chain.Initialize(48000, 2);
        float[] samples = [0.75f, -0.25f];
        chain.Process(samples);
        Assert.Equal(new float[] { 0.75f, -0.25f }, samples);
    }

    [Fact]
    public void RecognizesList() => Assert.Equal(CommandKind.List, CommandLine.Parse(["list"]).Kind);

    [Fact]
    public void LoadsChainFromLocalFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"processors":[{"type":"distortion","parameters":{"drive":2,"outputGain":0.5}}]}""");
            var options = CommandLine.Parse(["run", "--chain", path]).Options;
            Assert.Equal(path, options.ChainPath);
            var chain = options.CreateProcessorChain();
            chain.Initialize(48000, 1);
            float[] samples = [0.2f];
            chain.Process(samples);
            Assert.Equal(MathF.Tanh(0.4f) * 0.5f, samples[0]);

            File.WriteAllText(path, "invalid json");
            Assert.Throws<System.Text.Json.JsonException>(() => options.CreateProcessorChain());
            File.Delete(path);
            Assert.Throws<FileNotFoundException>(() => options.CreateProcessorChain());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsEmptyChainPath() =>
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(["run", "--chain", " "]));

    [Theory]
    [InlineData("other", "Use 'list'")]
    [InlineData("list --bypass", "takes no options")]
    [InlineData("run --unknown 1", "Unknown option --unknown")]
    [InlineData("run --drive", "Missing value for --drive")]
    [InlineData("run --input --output 0", "Missing value for --input")]
    [InlineData("run --input 0 --input 1", "Duplicate option --input")]
    [InlineData("run --bypass --bypass", "Duplicate option --bypass")]
    [InlineData("run --input invalid", "--input must be an integer")]
    [InlineData("run --seconds 2147483648", "--seconds must be an integer")]
    [InlineData("run --wet invalid", "--wet must be a number")]
    [InlineData("run --seconds 0", "--seconds must be positive")]
    [InlineData("run --seconds -1", "--seconds must be positive")]
    [InlineData("run --chain", "Missing value for --chain")]
    [InlineData("run --chain a.json --chain b.json", "Duplicate option --chain")]
    [InlineData("run --chain a.json --drive 2", "--chain cannot be combined")]
    [InlineData("run --chain a.json --wet 0.1", "--chain cannot be combined")]
    [InlineData("run --chain a.json --bypass", "--chain cannot be combined")]
    public void RejectsInvalidArgumentsWithOptionContext(string arguments, string message)
    {
        var error = Assert.Throws<ArgumentException>(() => CommandLine.Parse(arguments.Split(' ')));
        Assert.Contains(message, error.Message);
    }
}
