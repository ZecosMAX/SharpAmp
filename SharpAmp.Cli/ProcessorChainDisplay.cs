using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Processors;
using SharpAmp.Core.Dsp.Plugins;

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
        VstPluginProcessor effect => $"{effect.Name} ({effect.Format}) — {effect.PluginPath}",
        GainProcessor effect => FormattableString.Invariant($"Gain — gain: {effect.Gain}"),
        HardOverdriveProcessor effect => FormattableString.Invariant(
            $"Hard overdrive — drive: {effect.Drive}, output gain: {effect.OutputGain}"),
        DistortionProcessor effect => FormattableString.Invariant(
            $"Distortion — drive: {effect.Drive}, output gain: {effect.OutputGain}, wet mix: {effect.Wet}"),
        DelayProcessor effect => FormattableString.Invariant(
            $"Delay — time ms: {effect.TimeMs}, feedback: {effect.Feedback}, wet mix: {effect.Wet}"),
        TapeDelayProcessor effect => FormattableString.Invariant(
            $"Tape delay — time ms: {effect.TimeMs}, feedback: {effect.Feedback}, wet mix: {effect.Wet}, tone Hz: {effect.ToneHz}, wow depth ms: {effect.WowDepthMs}, wow rate Hz: {effect.WowRateHz}"),
        TremoloProcessor effect => FormattableString.Invariant(
            $"Tremolo — rate Hz: {effect.RateHz}, depth: {effect.Depth}"),
        BitcrusherProcessor effect => FormattableString.Invariant(
            $"Bitcrusher — bits: {effect.Bits}, downsample: {effect.Downsample}, wet mix: {effect.Wet}"),
        OctaveFuzzProcessor effect => FormattableString.Invariant(
            $"Octave fuzz — drive: {effect.Drive}, output gain: {effect.OutputGain}, wet mix: {effect.Wet}"),
        RingModulatorProcessor effect => FormattableString.Invariant(
            $"Ring modulator — frequency Hz: {effect.FrequencyHz}, wet mix: {effect.Wet}"),
        ReverbProcessor effect => FormattableString.Invariant(
            $"Reverb — wet mix: {effect.Wet}, feedback: {effect.Feedback}, damping: {effect.Damping}"),
        _ => processor.GetType().Name
    };
}
