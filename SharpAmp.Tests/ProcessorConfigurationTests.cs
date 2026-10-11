using System.Globalization;
using System.Text.Json;
using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Configuration;
using SharpAmp.Core.Dsp.Processors;

namespace SharpAmp.Tests;

public sealed class ProcessorConfigurationTests
{
    [Fact]
    public void JsonChainMatchesExplicitProcessorsIncludingOrderAndAllSettings()
    {
        var actual = ProcessorChain.FromJson("""
            {"processors":[
                {"type":"reverb","parameters":{"wet":0.6,"feedback":0.4,"damping":0.15}},
                {"type":"hardOverdrive","parameters":{"drive":2,"outputGain":0.8}},
                {"type":"distortion","parameters":{"drive":4,"outputGain":0.5}},
                {"type":"reverb","parameters":{"wet":0.3,"feedback":0.8,"damping":0.6}}
            ]}
            """);
        var expected = new ProcessorChain(new ReverbProcessor(0.6f, 0.4f, 0.15f),
            new HardOverdriveProcessor(2, 0.8f), new DistortionProcessor(4, 0.5f),
            new ReverbProcessor(0.3f, 0.8f, 0.6f));
        AssertEquivalent(expected, actual);
    }

    [Theory]
    [InlineData("hardOverdrive")]
    [InlineData("distortion")]
    [InlineData("reverb")]
    [InlineData("gain")]
    [InlineData("delay")]
    [InlineData("tapeDelay")]
    [InlineData("tremolo")]
    [InlineData("bitcrusher")]
    [InlineData("octaveFuzz")]
    [InlineData("ringModulator")]
    public void OmittedParametersUseConstructorDefaults(string type)
    {
        IDigitalSignalProcessor expected = type switch
        {
            "hardOverdrive" => new HardOverdriveProcessor(),
            "distortion" => new DistortionProcessor(),
            "gain" => new GainProcessor(),
            "delay" => new DelayProcessor(),
            "tapeDelay" => new TapeDelayProcessor(),
            "tremolo" => new TremoloProcessor(),
            "bitcrusher" => new BitcrusherProcessor(),
            "octaveFuzz" => new OctaveFuzzProcessor(),
            "ringModulator" => new RingModulatorProcessor(),
            _ => new ReverbProcessor()
        };
        AssertEquivalent(expected, ProcessorChain.FromJson($$"""{"processors":[{"type":"{{type}}"}]}"""));
    }

    [Fact]
    public void ModelRoundTripsAcrossCulturesAndBuildsIndependentSnapshots()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var configuration = new ProcessorChainConfiguration
            {
                Processors = [new() { Type = "reverb", Parameters = new() { ["wet"] = 0.75f } }]
            };
            var json = configuration.ToJson();
            var restored = ProcessorChainConfiguration.FromJson(json);
            Assert.Equal(0.75f, restored.Processors[0].Parameters["wet"]);
            var first = ProcessorChain.FromConfiguration(restored);
            var second = ProcessorChain.FromConfiguration(restored);
            restored.Processors[0].Parameters["wet"] = 0;
            restored.Processors.Clear();
            AssertEquivalent(new ReverbProcessor(0.75f), first);
            second.Initialize(48000, 2);
            var silence = new float[16000];
            second.Process(silence);
            Assert.All(silence, sample => Assert.Equal(0, sample));
            first.Reset();
            first.Process(silence);
            Assert.All(silence, sample => Assert.Equal(0, sample));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void EmptyPresetIsBypassAndStillRequiresInitialization()
    {
        var chain = ProcessorChain.FromJson("""{"processors":[]}""");
        float[] samples = [0.25f, -0.5f];
        Assert.Throws<InvalidOperationException>(() => chain.Process(samples));
        chain.Initialize(48000, 2);
        chain.Process(samples);
        Assert.Equal(new float[] { 0.25f, -0.5f }, samples);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"processors\":{}}")]
    [InlineData("{\"processors\":[],\"typo\":1}")]
    [InlineData("{\"processors\":[],\"processors\":[]}")]
    [InlineData("{\"processors\":[{}]}")]
    [InlineData("{\"processors\":[{\"type\":\"reverb\",\"paramters\":{}}]}")]
    [InlineData("{\"processors\":[{\"type\":\"reverb\",\"parameters\":{\"wet\":\"0.2\"}}]}")]
    [InlineData("{\"processors\":[{\"type\":\"reverb\",\"parameters\":{\"wet\":null}}]}")]
    [InlineData("{\"processors\":[{\"type\":\"reverb\",\"parameters\":{\"wet\":0.2,\"wet\":0.5}}]}")]
    public void RejectsMalformedOrAmbiguousJson(string json) =>
        Assert.Throws<JsonException>(() => ProcessorChain.FromJson(json));

    [Theory]
    [InlineData("null", "processors[0]")]
    [InlineData("{\"type\":null}", "processors[0].type")]
    [InlineData("{\"type\":\"\"}", "processors[0].type")]
    [InlineData("{\"type\":\"unknown\"}", "unknown effect")]
    [InlineData("{\"type\":\"Reverb\"}", "unknown effect")]
    [InlineData("{\"type\":\"reverb\",\"parameters\":null}", "parameters")]
    [InlineData("{\"type\":\"reverb\",\"parameters\":{\"Wet\":0.2}}", "unknown parameter")]
    [InlineData("{\"type\":\"hardOverdrive\",\"parameters\":{\"drive\":0}}", "parameters.drive")]
    [InlineData("{\"type\":\"distortion\",\"parameters\":{\"outputGain\":2}}", "parameters.outputGain")]
    [InlineData("{\"type\":\"reverb\",\"parameters\":{\"wet\":-1}}", "parameters.wet")]
    [InlineData("{\"type\":\"reverb\",\"parameters\":{\"feedback\":1}}", "parameters.feedback")]
    [InlineData("{\"type\":\"reverb\",\"parameters\":{\"damping\":1}}", "parameters.damping")]
    [InlineData("{\"type\":\"reverb\",\"parameters\":{\"wet\":1e100}}", "must be finite")]
    public void RejectsInvalidEffectsWithContext(string processor, string message)
    {
        var error = Assert.Throws<ArgumentException>(() => ProcessorChain.FromJson($$"""{"processors":[{{processor}}]}"""));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void RejectsNullInputsAndNonfiniteModelValues()
    {
        Assert.Throws<ArgumentNullException>(() => ProcessorChain.FromJson(null!));
        Assert.Throws<ArgumentNullException>(() => ProcessorChain.FromConfiguration(null!));
        Assert.Throws<ArgumentException>(() => ProcessorChain.FromJson("""{"processors":null}"""));
        var configuration = new ProcessorChainConfiguration
        {
            Processors = [new() { Type = "distortion", Parameters = new() { ["drive"] = float.NaN } }]
        };
        Assert.Throws<ArgumentException>(() => ProcessorChain.FromConfiguration(configuration));
    }

    private static void AssertEquivalent(IDigitalSignalProcessor expected, IDigitalSignalProcessor actual)
    {
        expected.Initialize(48000, 2);
        actual.Initialize(48000, 2);
        var input = new float[48000];
        input[0] = 0.8f;
        input[9] = -0.4f;
        var output = (float[])input.Clone();
        expected.Process(input);
        actual.Process(output);
        Assert.Equal(input, output);
    }
}
