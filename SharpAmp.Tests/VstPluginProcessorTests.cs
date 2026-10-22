using SharpAmp.Avalonia.ViewModels;
using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Configuration;
using SharpAmp.Core.Dsp.Plugins;

namespace SharpAmp.Tests;

public sealed class VstPluginProcessorTests
{
    [Fact]
    public void ProcessesVariableInterleavedBlocksWithoutAllocatingAndSnapshotsParameters()
    {
        var native = new FakePlugin();
        var parameters = new Dictionary<uint, float> { [7] = 0.25f };
        using var effect = new VstPluginProcessor(@"C:\Effects\test.dll", VstPluginFormat.Vst2, parameters, () => native);
        parameters[7] = 0.9f;
        effect.Initialize(48000, 2);
        Assert.Equal(0.25f, native.Value);
        var samples = Enumerable.Range(0, 5002).Select(i => i % 2 == 0 ? 0.8f : -0.4f).ToArray();
        effect.Process(samples);
        Assert.Equal(3, native.Calls);
        Assert.Equal(453, native.LastFrames);
        Assert.Equal(0.2f, samples[0]);
        Assert.Equal(-0.1f, samples[^1]);
        var before = GC.GetAllocatedBytesForCurrentThread();
        effect.Process(samples);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Throws<ArgumentException>(() => effect.Process(new float[3]));
    }

    [Fact]
    public void FailedParameterInitializationDisposesNativeInstance()
    {
        var native = new FakePlugin();
        using var effect = new VstPluginProcessor(@"C:\Effects\test.vst3", VstPluginFormat.Vst3,
            new Dictionary<uint, float> { [9] = 1 }, () => native);
        Assert.Throws<ArgumentException>(() => effect.Initialize(48000, 1));
        Assert.Equal(1, native.Disposals);
        Assert.Throws<InvalidOperationException>(() => effect.Process(new float[2]));
    }

    [Fact]
    public void ResetReloadsAndReappliesSettingsAndDisposeIsIdempotent()
    {
        var instances = new List<FakePlugin>();
        var effect = new VstPluginProcessor(@"C:\Effects\test.dll", VstPluginFormat.Vst2,
            new Dictionary<uint, float> { [7] = 0.4f }, () =>
            {
                var instance = new FakePlugin();
                instances.Add(instance);
                return instance;
            });
        effect.Initialize(44100, 1);
        effect.Reset();
        Assert.Equal(1, instances[0].Disposals);
        Assert.Equal(0.4f, instances[1].Value);
        effect.Dispose();
        effect.Dispose();
        Assert.Equal(1, instances[1].Disposals);
        Assert.Throws<ObjectDisposedException>(() => effect.Initialize(48000, 2));
    }

    [Theory]
    [InlineData("vst2", @"C:\Effects\test.dll")]
    [InlineData("vst3", @"C:\Effects\test.vst3")]
    public void CoreAndDesktopPresetsPreservePathsParametersAndBypassWithoutLoadingNativeCode(string type, string path)
    {
        var preset = new ProcessorChainConfiguration
        {
            Processors = [new ProcessorConfiguration { Type = type, PluginPath = path, Parameters = new() { ["7"] = 0.3f } }]
        };
        using var chain = ProcessorChain.FromJson(preset.ToJson());
        var effect = Assert.IsType<VstPluginProcessor>(Assert.Single(chain.Processors));
        Assert.Equal(path, effect.PluginPath);
        Assert.Equal(0.3f, effect.ParameterValues[7]);
        var board = new ProcessorChainViewModel();
        board.LoadPresetJson(preset.ToJson());
        board.SelectedProcessor!.IsBypassed = true;
        var duplicate = board.DuplicateSelected()!;
        Assert.Equal(path, duplicate.PluginPath);
        var restored = new ProcessorChainViewModel();
        restored.LoadPresetJson(board.ToPresetJson());
        Assert.Equal(board.ToPresetJson(), restored.ToPresetJson());
        Assert.All(restored.Processors, p => Assert.True(p.IsBypassed));
        Assert.Equal(0.3f, restored.Processors[0].ToConfiguration().Parameters["7"]);
    }

    [Theory]
    [InlineData("relative.dll", "0", 0.5f)]
    [InlineData(@"C:\Effects\wrong.vst3", "0", 0.5f)]
    [InlineData(@"C:\Effects\test.dll", "gain", 0.5f)]
    [InlineData(@"C:\Effects\test.dll", "01", 0.5f)]
    [InlineData(@"C:\Effects\test.dll", "0", -0.1f)]
    [InlineData(@"C:\Effects\test.dll", "0", 1.1f)]
    public void InvalidPluginConfigurationFailsBeforePublication(string path, string parameter, float value)
    {
        Assert.ThrowsAny<ArgumentException>(() => ProcessorChain.FromConfiguration(new ProcessorChainConfiguration
        {
            Processors = [new ProcessorConfiguration { Type = "vst2", PluginPath = path, Parameters = new() { [parameter] = value } }]
        }));
    }

    [Fact]
    public void BuiltInEffectsRejectPluginPaths()
    {
        Assert.Throws<ArgumentException>(() => ProcessorChain.FromConfiguration(new ProcessorChainConfiguration
        { Processors = [new ProcessorConfiguration { Type = "gain", PluginPath = @"C:\Effects\test.dll" }] }));
    }

    private sealed class FakePlugin : IVstPlugin
    {
        public string Name => "Test effect";
        public int InputChannels => 2;
        public int OutputChannels => 2;
        public IReadOnlyList<VstParameter> Parameters => [new(7, "Level", 1)];
        private int channels;
        public float Value { get; private set; } = 1;
        public int Calls { get; private set; }
        public int LastFrames { get; private set; }
        public int Disposals { get; private set; }
        public void Initialize(int sampleRate, int channels, int maxBlockSize) => this.channels = channels;
        public void SetParameter(uint id, float value)
        {
            if (id != 7) throw new ArgumentException("Unknown parameter.");
            Value = value;
        }
        public void Process(float[] samples, int frameCount)
        {
            Calls++;
            LastFrames = frameCount;
            for (var i = 0; i < frameCount * channels; i++) samples[i] *= Value;
        }
        public void Dispose() => Disposals++;
    }
}
