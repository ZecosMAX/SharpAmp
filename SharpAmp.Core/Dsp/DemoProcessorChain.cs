using SharpAmp.Core.Dsp.Processors;

namespace SharpAmp.Core.Dsp;

public static class DemoProcessorChain
{
    public static ProcessorChain Create(float drive = 5, float wet = 0.2f) =>
        new(new HardOverdriveProcessor(drive), new ReverbProcessor(wet));
}
