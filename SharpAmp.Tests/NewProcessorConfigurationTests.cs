using System.Globalization;
using SharpAmp.Cli;
using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Configuration;
using SharpAmp.Core.Dsp.Processors;

namespace SharpAmp.Tests;

public sealed class NewProcessorConfigurationTests
{
    [Fact]
    public void NewEffectsLoadAllSettingsAndDisplayThemInOrder()
    {
        var actual = ProcessorChain.FromJson("""
            {"processors":[
                {"type":"gain","parameters":{"gain":1.25}},
                {"type":"delay","parameters":{"timeMs":20,"feedback":0.2,"wet":0.3}},
                {"type":"tapeDelay","parameters":{"timeMs":30,"feedback":0.3,"wet":0.4,"toneHz":2500,"wowDepthMs":1.5,"wowRateHz":0.7}},
                {"type":"tremolo","parameters":{"rateHz":5,"depth":0.6}},
                {"type":"bitcrusher","parameters":{"bits":6,"downsample":3,"wet":0.8}},
                {"type":"octaveFuzz","parameters":{"drive":7,"outputGain":0.4,"wet":0.9}},
                {"type":"ringModulator","parameters":{"frequencyHz":123,"wet":0.6}},
                {"type":"distortion","parameters":{"drive":2,"outputGain":0.7,"wet":0.3}}
            ]}
            """);
        var expected = new ProcessorChain(new GainProcessor(1.25f), new DelayProcessor(20, 0.2f, 0.3f),
            new TapeDelayProcessor(30, 0.3f, 0.4f, 2500, 1.5f, 0.7f), new TremoloProcessor(5, 0.6f),
            new BitcrusherProcessor(6, 3, 0.8f), new OctaveFuzzProcessor(7, 0.4f, 0.9f),
            new RingModulatorProcessor(123, 0.6f), new DistortionProcessor(2, 0.7f, 0.3f));
        actual.Initialize(48000, 2);
        expected.Initialize(48000, 2);
        var first = Enumerable.Range(0, 96000).Select(i => (float)(0.5 * Math.Sin(i * 0.03))).ToArray();
        var second = (float[])first.Clone();
        expected.Process(first);
        actual.Process(second);
        Assert.Equal(first, second);

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            using var writer = new StringWriter();
            ProcessorChainDisplay.Write(writer, actual);
            var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(9, lines.Length);
            Assert.Equal("  1. Gain — gain: 1.25", lines[1]);
            Assert.Equal("  2. Delay — time ms: 20, feedback: 0.2, wet mix: 0.3", lines[2]);
            Assert.Equal("  3. Tape delay — time ms: 30, feedback: 0.3, wet mix: 0.4, tone Hz: 2500, wow depth ms: 1.5, wow rate Hz: 0.7", lines[3]);
            Assert.Equal("  4. Tremolo — rate Hz: 5, depth: 0.6", lines[4]);
            Assert.Equal("  5. Bitcrusher — bits: 6, downsample: 3, wet mix: 0.8", lines[5]);
            Assert.Equal("  6. Octave fuzz — drive: 7, output gain: 0.4, wet mix: 0.9", lines[6]);
            Assert.Equal("  7. Ring modulator — frequency Hz: 123, wet mix: 0.6", lines[7]);
            Assert.Equal("  8. Distortion — drive: 2, output gain: 0.7, wet mix: 0.3", lines[8]);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("gain", "gain", -1)]
    [InlineData("delay", "timeMs", 0)]
    [InlineData("delay", "timeMs", 2001)]
    [InlineData("delay", "feedback", 1)]
    [InlineData("delay", "feedback", -0.1)]
    [InlineData("delay", "wet", 1.1)]
    [InlineData("tapeDelay", "timeMs", 2001)]
    [InlineData("tapeDelay", "feedback", 1)]
    [InlineData("tapeDelay", "wet", -0.1)]
    [InlineData("tapeDelay", "toneHz", 19)]
    [InlineData("tapeDelay", "toneHz", 20001)]
    [InlineData("tapeDelay", "wowDepthMs", -1)]
    [InlineData("tapeDelay", "wowDepthMs", 11)]
    [InlineData("tapeDelay", "wowRateHz", 11)]
    [InlineData("tremolo", "rateHz", -1)]
    [InlineData("tremolo", "rateHz", 21)]
    [InlineData("tremolo", "depth", 1.1)]
    [InlineData("bitcrusher", "bits", 1)]
    [InlineData("bitcrusher", "bits", 17)]
    [InlineData("bitcrusher", "bits", 8.5)]
    [InlineData("bitcrusher", "bits", 2147483648)]
    [InlineData("bitcrusher", "downsample", 0)]
    [InlineData("bitcrusher", "downsample", 65)]
    [InlineData("bitcrusher", "downsample", 2.5)]
    [InlineData("bitcrusher", "wet", 1.1)]
    [InlineData("octaveFuzz", "drive", 0)]
    [InlineData("octaveFuzz", "outputGain", 1.1)]
    [InlineData("octaveFuzz", "wet", -0.1)]
    [InlineData("ringModulator", "frequencyHz", -1)]
    [InlineData("ringModulator", "frequencyHz", 20001)]
    [InlineData("ringModulator", "wet", 1.1)]
    [InlineData("distortion", "wet", -0.1)]
    public void InvalidSettingsReportProcessorAndParameter(string type, string parameter, double value)
    {
        var json = $$$"""{"processors":[{"type":"{{{type}}}","parameters":{"{{{parameter}}}":{{{value.ToString(CultureInfo.InvariantCulture)}}}}}]}""";
        var error = Assert.Throws<ArgumentException>(() => ProcessorChain.FromJson(json));
        Assert.Contains($"processors[0].parameters.{parameter}", error.Message);
    }

    [Theory]
    [InlineData("gain")]
    [InlineData("delay")]
    [InlineData("tapeDelay")]
    [InlineData("tremolo")]
    [InlineData("bitcrusher")]
    [InlineData("octaveFuzz")]
    [InlineData("ringModulator")]
    public void UnknownParametersAreRejected(string type) => Assert.Throws<ArgumentException>(() =>
        ProcessorChain.FromJson($$$"""{"processors":[{"type":"{{{type}}}","parameters":{"typo":1}}]}"""));

    [Fact]
    public void ConstructorsRejectNonfiniteFloatParameters()
    {
        Type[] types = [typeof(GainProcessor), typeof(DelayProcessor), typeof(TapeDelayProcessor),
            typeof(TremoloProcessor), typeof(BitcrusherProcessor), typeof(OctaveFuzzProcessor),
            typeof(RingModulatorProcessor), typeof(DistortionProcessor)];
        foreach (var type in types)
        {
            var constructor = Assert.Single(type.GetConstructors());
            var parameters = constructor.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType != typeof(float)) continue;
                foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    var arguments = parameters.Select(p => p.DefaultValue).ToArray();
                    arguments[i] = invalid;
                    var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => constructor.Invoke(arguments));
                    Assert.Equal(parameters[i].Name, Assert.IsType<ArgumentOutOfRangeException>(error.InnerException).ParamName);
                }
            }
        }
    }

    [Theory]
    [InlineData("guitar_(clean)")]
    [InlineData("guitar_(distorted)")]
    [InlineData("guitar_(heavy)")]
    [InlineData("bass_(clean)")]
    [InlineData("bass_(distorted)")]
    [InlineData("bass_(heavy)")]
    [InlineData("overdrive-room")]
    public void ShippedPresetsLoadThroughCliAndRemainFiniteWithFullScaleInput(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "presets", name + ".json");
        var chain = CommandLine.Parse(["run", "--chain", path]).Options.CreateProcessorChain();
        var model = ProcessorChainConfiguration.FromJson(File.ReadAllText(path));
        Assert.Equal(model.Processors.Count, ProcessorChain.FromJson(model.ToJson()).Processors.Count);
        chain.Initialize(48000, 2);
        var samples = Enumerable.Range(0, 96000).Select(i => (float)Math.Sin(2 * Math.PI * 82 * (i / 2) / 48000)).ToArray();
        chain.Process(samples);
        Assert.All(samples, value => Assert.True(float.IsFinite(value) && Math.Abs(value) <= 1));
        Assert.Contains(samples, value => Math.Abs(value) > 0.1);
    }
}
