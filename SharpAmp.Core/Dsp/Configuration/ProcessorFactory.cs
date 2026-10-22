using SharpAmp.Core.Dsp.Processors;
using SharpAmp.Core.Dsp.Plugins;
using System.Globalization;

namespace SharpAmp.Core.Dsp.Configuration;

internal static class ProcessorFactory
{
    public static IDigitalSignalProcessor Create(ProcessorConfiguration configuration, int index)
    {
        var context = $"processors[{index}]";
        if (configuration is null)
            throw new ArgumentException($"{context} must be an object.");
        if (string.IsNullOrWhiteSpace(configuration.Type))
            throw new ArgumentException($"{context}.type must name an effect.");
        if (configuration.Parameters is null)
            throw new ArgumentException($"{context}.parameters must be an object.");

        // Copy with a fixed comparer so programmatic and JSON presets use the same naming rules.
        var parameters = new Dictionary<string, float>(configuration.Parameters, StringComparer.Ordinal);
        if (configuration.Type is "vst2" or "vst3")
        {
            var values = new Dictionary<uint, float>();
            foreach (var (key, value) in parameters)
            {
                if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    || key != id.ToString(CultureInfo.InvariantCulture))
                    throw new ArgumentException($"{context}.parameters: '{key}' must be a canonical unsigned parameter ID.");
                values.Add(id, value);
            }
            return new VstPluginProcessor(configuration.PluginPath!,
                configuration.Type == "vst2" ? VstPluginFormat.Vst2 : VstPluginFormat.Vst3, values);
        }
        if (configuration.PluginPath is not null)
            throw new ArgumentException($"{context}.pluginPath is only valid for vst2/vst3 effects.");
        string[] allowed = configuration.Type switch
        {
            "gain" => ["gain"],
            "hardOverdrive" => ["drive", "outputGain"],
            "distortion" or "octaveFuzz" => ["drive", "outputGain", "wet"],
            "reverb" => ["wet", "feedback", "damping"],
            "delay" => ["timeMs", "feedback", "wet"],
            "tapeDelay" => ["timeMs", "feedback", "wet", "toneHz", "wowDepthMs", "wowRateHz"],
            "tremolo" => ["rateHz", "depth"],
            "bitcrusher" => ["bits", "downsample", "wet"],
            "ringModulator" => ["frequencyHz", "wet"],
            _ => throw new ArgumentException($"{context}.type: unknown effect '{configuration.Type}'.")
        };
        foreach (var (key, value) in parameters)
        {
            if (!allowed.Contains(key, StringComparer.Ordinal))
                throw new ArgumentException($"{context}.parameters: unknown parameter '{key}' for '{configuration.Type}'.");
            if (!float.IsFinite(value))
                throw new ArgumentException($"{context}.parameters.{key} must be finite.");
        }

        try
        {
            return configuration.Type switch
            {
                "gain" => new GainProcessor(parameters.GetValueOrDefault("gain", 1)),
                "hardOverdrive" => new HardOverdriveProcessor(
                    parameters.GetValueOrDefault("drive", 5), parameters.GetValueOrDefault("outputGain", 0.35f)),
                "distortion" => new DistortionProcessor(
                    parameters.GetValueOrDefault("drive", 3), parameters.GetValueOrDefault("outputGain", 0.35f),
                    parameters.GetValueOrDefault("wet", 1)),
                "octaveFuzz" => new OctaveFuzzProcessor(
                    parameters.GetValueOrDefault("drive", 5), parameters.GetValueOrDefault("outputGain", 0.35f),
                    parameters.GetValueOrDefault("wet", 1)),
                "delay" => new DelayProcessor(parameters.GetValueOrDefault("timeMs", 300),
                    parameters.GetValueOrDefault("feedback", 0.35f), parameters.GetValueOrDefault("wet", 0.25f)),
                "tapeDelay" => new TapeDelayProcessor(parameters.GetValueOrDefault("timeMs", 350),
                    parameters.GetValueOrDefault("feedback", 0.4f), parameters.GetValueOrDefault("wet", 0.25f),
                    parameters.GetValueOrDefault("toneHz", 3000), parameters.GetValueOrDefault("wowDepthMs", 2),
                    parameters.GetValueOrDefault("wowRateHz", 0.5f)),
                "tremolo" => new TremoloProcessor(parameters.GetValueOrDefault("rateHz", 4),
                    parameters.GetValueOrDefault("depth", 0.5f)),
                "bitcrusher" => new BitcrusherProcessor(IntegerParameter(parameters, "bits", 8),
                    IntegerParameter(parameters, "downsample", 4), parameters.GetValueOrDefault("wet", 1)),
                "ringModulator" => new RingModulatorProcessor(parameters.GetValueOrDefault("frequencyHz", 440),
                    parameters.GetValueOrDefault("wet", 0.5f)),
                "reverb" => new ReverbProcessor(
                    parameters.GetValueOrDefault("wet", 0.2f), parameters.GetValueOrDefault("feedback", 0.7f),
                    parameters.GetValueOrDefault("damping", 0.3f)),
                _ => throw new InvalidOperationException("Unrecognized validated processor type.")
            };
        }
        catch (ArgumentOutOfRangeException error)
        {
            throw new ArgumentException($"{context}.parameters.{error.ParamName}: invalid value for '{configuration.Type}'.", error);
        }
    }

    private static int IntegerParameter(Dictionary<string, float> parameters, string name, int defaultValue)
    {
        var value = parameters.GetValueOrDefault(name, defaultValue);
        if (value != MathF.Truncate(value) || value < int.MinValue || (double)value > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, "Must be an integer.");
        return (int)value;
    }
}
