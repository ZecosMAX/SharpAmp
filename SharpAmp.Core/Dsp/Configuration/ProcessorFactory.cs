using SharpAmp.Core.Dsp.Processors;

namespace SharpAmp.Core.Dsp.Configuration;

internal static class ProcessorFactory
{
    public static IDigitalSignalProcessor Create(ProcessorConfiguration configuration, int index)
    {
        var context = $"processors[{index}]";
        if (configuration is null)
            throw new ArgumentException($"{context} must be an object.");
        if (string.IsNullOrWhiteSpace(configuration.Type))
            throw new ArgumentException($"{context}.type must name a built-in effect.");
        if (configuration.Parameters is null)
            throw new ArgumentException($"{context}.parameters must be an object.");

        // Copy with a fixed comparer so programmatic and JSON presets use the same naming rules.
        var parameters = new Dictionary<string, float>(configuration.Parameters, StringComparer.Ordinal);
        string[] allowed = configuration.Type switch
        {
            "hardOverdrive" or "distortion" => ["drive", "outputGain"],
            "reverb" => ["wet", "feedback", "damping"],
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
                "hardOverdrive" => new HardOverdriveProcessor(
                    parameters.GetValueOrDefault("drive", 5), parameters.GetValueOrDefault("outputGain", 0.35f)),
                "distortion" => new DistortionProcessor(
                    parameters.GetValueOrDefault("drive", 3), parameters.GetValueOrDefault("outputGain", 0.35f)),
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
}
