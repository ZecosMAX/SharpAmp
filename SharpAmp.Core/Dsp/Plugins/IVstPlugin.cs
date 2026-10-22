namespace SharpAmp.Core.Dsp.Plugins;

/// <summary>A plug-in parameter with a normalized value in [0, 1]. IDs are specific to the plug-in.</summary>
public sealed record VstParameter(uint Id, string Name, float DefaultValue);

internal interface IVstPlugin : IDisposable
{
    string Name { get; }
    int InputChannels { get; }
    int OutputChannels { get; }
    IReadOnlyList<VstParameter> Parameters { get; }
    void Initialize(int sampleRate, int channels, int maxBlockSize);
    void Process(float[] interleaved, int frameCount);
    void SetParameter(uint id, float value);
}
