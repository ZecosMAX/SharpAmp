using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Processors;

namespace SharpAmp.Cli;

internal static class ProcessorChainDisplay
{
    public static void Write(TextWriter writer, ProcessorChain chain)
    {
        if (chain.Processors.Count == 0)
        {
            writer.WriteLine("Processor chain: bypass (no effects)");
            return;
        }

        writer.WriteLine("Processor chain (in processing order):");
        for (var i = 0; i < chain.Processors.Count; i++)
            writer.WriteLine($"  {i + 1}. {Describe(chain.Processors[i])}");
    }

    private static string Describe(IDigitalSignalProcessor processor) => processor switch
    {
        HardOverdriveProcessor effect => FormattableString.Invariant(
            $"Hard overdrive — drive: {effect.Drive}, output gain: {effect.OutputGain}"),
        DistortionProcessor effect => FormattableString.Invariant(
            $"Distortion — drive: {effect.Drive}, output gain: {effect.OutputGain}"),
        ReverbProcessor effect => FormattableString.Invariant(
            $"Reverb — wet mix: {effect.Wet}, feedback: {effect.Feedback}, damping: {effect.Damping}"),
        _ => processor.GetType().Name
    };
}
