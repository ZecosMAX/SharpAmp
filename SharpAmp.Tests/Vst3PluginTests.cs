using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Configuration;
using SharpAmp.Core.Dsp.Plugins;

namespace SharpAmp.Tests;

public sealed class Vst3PluginTests
{
    [Fact]
    public void BundleResolvesItsWindowsX64Module()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SharpAmp-vst3-test-" + Guid.NewGuid().ToString("N"));
        var bundle = Path.Combine(directory, "Test.vst3");
        var binaries = Path.Combine(bundle, "Contents", "x86_64-win");
        Directory.CreateDirectory(binaries);
        try
        {
            var module = Path.Combine(binaries, "Test.vst3");
            File.WriteAllBytes(module, []);
            Assert.Equal(module, Vst3Plugin.ResolveModulePath(bundle));
            Assert.Equal(module, Vst3Plugin.ResolveModulePath(module));
            File.Delete(module);
            Assert.Throws<FileNotFoundException>(() => Vst3Plugin.ResolveModulePath(bundle));
        }
        finally
        {
            // This path is created above exclusively for this test.
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingPluginFailsBeforeLoadingNativeCode()
    {
        using var plugin = new Vst3Plugin(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vst3"));
        Assert.Throws<FileNotFoundException>(() => plugin.Initialize(48000, 2, 1024));
        plugin.Dispose();
    }

    [Fact]
    public void InvalidBinaryFailsBeforeCallingPluginEntryPoints()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vst3");
        try
        {
            File.WriteAllText(file, "This is not a plug-in.");
            using var plugin = new Vst3Plugin(file);
            Assert.Throws<BadImageFormatException>(() => plugin.Initialize(48000, 2, 1024));
        }
        finally { File.Delete(file); }
    }
}

// Opt-in tests exercise an installed third-party binary in the test process.
// Example: $env:SHARPAMP_TEST_VST3_PATH = 'C:\Program Files\Common Files\VST3\Tonocracy.vst3'
// dotnet test --filter FullyQualifiedName~Vst3NativeIntegrationTests
[Collection("VST3 native integration")]
public sealed class Vst3NativeIntegrationTests
{
    [Vst3IntegrationFact]
    public void LoadsParametersProcessesShortBlocksAndCanReload()
    {
        foreach (var channels in new[] { 1, 2 })
        {
            using var processor = CreateProcessor();
            processor.Initialize(48000, channels);
            Assert.NotEmpty(processor.Name);
            Assert.All(processor.Parameters, parameter => Assert.InRange(parameter.DefaultValue, 0, 1));
            foreach (var frames in new[] { 1, 7, 512, 1024, 37, 2049 })
            {
                var samples = Enumerable.Repeat(0.125f, frames * channels + 8).ToArray();
                processor.Process(samples.AsSpan(0, frames * channels));
                Assert.All(samples.Take(frames * channels), sample => Assert.True(float.IsFinite(sample)));
                Assert.All(samples.Skip(frames * channels), sample => Assert.Equal(0.125f, sample));
            }
            processor.Reset();
            processor.Process(new float[64 * channels]);
        }
    }

    [Vst3IntegrationFact]
    public void ParameterValuesAreAppliedAndUnknownIdsAreRejected()
    {
        using var probe = CreateProcessor();
        probe.Initialize(48000, 2);
        var values = probe.Parameters.Take(4).ToDictionary(parameter => parameter.Id, parameter => parameter.DefaultValue);
        using var configured = CreateProcessor(values);
        configured.Initialize(48000, 2);
        configured.Process(new float[64]);

        var missingId = uint.MaxValue;
        while (probe.Parameters.Any(parameter => parameter.Id == missingId)) missingId--;
        using var invalid = CreateProcessor(new Dictionary<uint, float> { [missingId] = 0.5f });
        Assert.Throws<ArgumentException>(() => invalid.Initialize(48000, 2));
    }

    [Vst3IntegrationFact]
    public async Task ProcessingContinuesWhileOtherInstancesLoadAndUnload()
    {
        using var active = CreateProcessor();
        active.Initialize(48000, 2);
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        var blockCount = 0;
        var callback = Task.Run(() =>
        {
            var samples = new float[128];
            while (!cancellation.IsCancellationRequested)
            {
                Array.Fill(samples, 0.125f);
                active.Process(samples);
                Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
                Interlocked.Increment(ref blockCount);
                started.Set();
            }
        });
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            for (var index = 0; index < 3; index++)
            {
                var before = Volatile.Read(ref blockCount);
                using var replacement = CreateProcessor();
                replacement.Initialize(44100, 2);
                replacement.Process(new float[74]);
                Assert.True(Volatile.Read(ref blockCount) > before);
            }
        }
        finally
        {
            cancellation.Cancel();
            await callback.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Vst3IntegrationFact]
    public async Task RuntimeApiAddsAndRemovesNativeEffectWhileAudioRuns()
    {
        using var chain = new RuntimeProcessorChain();
        chain.Initialize(48000, 2);
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        var blocks = 0;
        var callback = Task.Run(() =>
        {
            var samples = new float[128];
            while (!cancellation.IsCancellationRequested)
            {
                Array.Fill(samples, 0.125f);
                chain.Process(samples);
                Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
                Interlocked.Increment(ref blocks);
                started.Set();
            }
        });
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            var index = chain.AddVst(Environment.GetEnvironmentVariable("SHARPAMP_TEST_VST3_PATH")!);
            Assert.Single(chain.Configurations);
            var addedAt = Volatile.Read(ref blocks);
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref blocks) > addedAt + 16, TimeSpan.FromSeconds(10)));
            chain.RemoveAt(index);
            Assert.Empty(chain.Configurations);
            var removedAt = Volatile.Read(ref blocks);
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref blocks) > removedAt + 16, TimeSpan.FromSeconds(10)));
        }
        finally
        {
            cancellation.Cancel();
            await callback.WaitAsync(TimeSpan.FromSeconds(30));
        }

        var dry = Enumerable.Repeat(0.125f, 2048).ToArray();
        chain.Process(dry);
        Assert.All(dry, sample => Assert.Equal(0.125f, sample));
        chain.Add(new ProcessorConfiguration { Type = "gain", Parameters = new() { ["gain"] = 0.5f } });
        var gained = Enumerable.Repeat(0.125f, 2048).ToArray();
        chain.Process(gained);
        Assert.All(gained.Skip(960), sample => Assert.Equal(0.0625f, sample));
    }

    private static VstPluginProcessor CreateProcessor(IReadOnlyDictionary<uint, float>? parameters = null) =>
        new(Environment.GetEnvironmentVariable("SHARPAMP_TEST_VST3_PATH")!, VstPluginFormat.Vst3, parameters);
}

public sealed class Vst3IntegrationFactAttribute : FactAttribute
{
    public Vst3IntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHARPAMP_TEST_VST3_PATH")))
            Skip = "Set SHARPAMP_TEST_VST3_PATH to a trusted installed Windows x64 VST3 effect to run native integration tests.";
    }
}

[CollectionDefinition("VST3 native integration", DisableParallelization = true)]
public sealed class Vst3NativeIntegrationCollection;
