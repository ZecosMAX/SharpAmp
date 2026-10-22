using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Configuration;

namespace SharpAmp.Tests;

public sealed class RuntimeProcessorChainTests
{
    [Fact]
    public void EffectsCanBeAddedBeforeAndDuringPlaybackAndRemoved()
    {
        using var chain = new RuntimeProcessorChain();
        Assert.Equal(0, chain.Add(Gain(0.5f)));
        chain.Initialize(1000, 1);
        Assert.Equal(0.5f, Process(chain));
        Assert.Equal(1, chain.Add(Gain(0.5f)));
        Assert.Equal(0.25f, Process(chain));
        chain.RemoveAt(0);
        Assert.Equal(0.5f, Process(chain));
        chain.ReplaceAt(0, Gain(0.2f));
        Assert.Equal(0.2f, Process(chain));
        chain.Clear();
        Assert.Equal(1, Process(chain));
    }

    [Fact]
    public void MissingPluginDoesNotChangePlayingChainOrConfiguration()
    {
        using var chain = new RuntimeProcessorChain();
        chain.Add(Gain(0.5f));
        chain.Initialize(48000, 2);
        Assert.ThrowsAny<Exception>(() => chain.AddVst(@"C:\Missing-SharpAmp-Test\effect.dll"));
        Assert.Single(chain.Configurations);
        float[] samples = [0.8f, -0.4f];
        chain.Process(samples);
        Assert.Equal(new float[] { 0.4f, -0.2f }, samples);
    }

    [Fact]
    public void ConfigurationsAreIndependentSnapshots()
    {
        using var chain = new RuntimeProcessorChain();
        var effect = Gain(0.4f);
        chain.Add(effect);
        effect.Parameters["gain"] = 0;
        chain.Configurations[0].Parameters["gain"] = 0;
        chain.Initialize(1000, 1);
        Assert.Equal(0.4f, Process(chain));
    }

    private static ProcessorConfiguration Gain(float gain) => new() { Type = "gain", Parameters = new() { ["gain"] = gain } };
    private static float Process(RuntimeProcessorChain chain)
    {
        var samples = Enumerable.Repeat(1f, 20).ToArray();
        chain.Process(samples);
        return samples[^1];
    }
}
