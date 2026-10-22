using System.Collections.ObjectModel;

namespace SharpAmp.Core.Dsp.Plugins;

public enum VstPluginFormat { Vst2, Vst3 }

/// <summary>
/// Hosts a native VST audio effect. Load/initialize on the control thread; process on one audio thread.
/// Dispose only after processing has stopped. The owning ProcessorChain handles disposal.
/// </summary>
public sealed class VstPluginProcessor : IDigitalSignalProcessor, IDisposable
{
    public const int MaxBlockSize = 1024;
    private readonly Func<IVstPlugin> createPlugin;
    private IVstPlugin? plugin;
    private float[] buffer = [];
    private int sampleRate;
    private int channels;
    private bool disposed;

    public string PluginPath { get; }
    public VstPluginFormat Format { get; }
    public string Name => plugin?.Name ?? Path.GetFileNameWithoutExtension(PluginPath);
    public IReadOnlyList<VstParameter> Parameters => plugin?.Parameters ?? [];
    public IReadOnlyDictionary<uint, float> ParameterValues { get; }

    public VstPluginProcessor(string pluginPath, VstPluginFormat format,
        IReadOnlyDictionary<uint, float>? parameters = null)
        : this(pluginPath, format, parameters, () => new VstPluginThread(() => format switch
        {
            VstPluginFormat.Vst2 => new Vst2Plugin(pluginPath),
            VstPluginFormat.Vst3 => new Vst3Plugin(pluginPath),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        })) { }

    internal VstPluginProcessor(string pluginPath, VstPluginFormat format,
        IReadOnlyDictionary<uint, float>? parameters, Func<IVstPlugin> createPlugin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        if (!Path.IsPathFullyQualified(pluginPath))
            throw new ArgumentException("The plug-in path must be absolute.", nameof(pluginPath));
        var extension = format == VstPluginFormat.Vst2 ? ".dll" : ".vst3";
        if (!pluginPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{format} plug-ins require a {extension} path.", nameof(pluginPath));
        var values = parameters is null ? new Dictionary<uint, float>() : new(parameters);
        foreach (var value in values.Values)
            if (!float.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentOutOfRangeException(nameof(parameters), "Plug-in parameters must be finite and between 0 and 1.");
        PluginPath = Path.GetFullPath(pluginPath);
        Format = format;
        ParameterValues = new ReadOnlyDictionary<uint, float>(values);
        this.createPlugin = createPlugin;
    }

    public void Initialize(int sampleRate, int channels)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (channels is not 1 and not 2)
            throw new NotSupportedException("VST effects currently support mono or stereo sessions.");
        this.channels = 0;
        plugin?.Dispose();
        plugin = null;
        var next = createPlugin();
        try
        {
            next.Initialize(sampleRate, channels, MaxBlockSize);
            foreach (var (id, value) in ParameterValues) next.SetParameter(id, value);
            buffer = new float[MaxBlockSize * channels];
            plugin = next;
            this.sampleRate = sampleRate;
            this.channels = channels;
        }
        catch
        {
            try { next.Dispose(); } catch { /* Preserve the load error. */ }
            throw;
        }
    }

    public void Process(Span<float> samples)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (channels == 0) throw new InvalidOperationException("Initialize the plug-in first.");
        if (samples.Length % channels != 0)
            throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        while (!samples.IsEmpty)
        {
            var count = Math.Min(samples.Length, buffer.Length);
            samples[..count].CopyTo(buffer);
            plugin!.Process(buffer, count / channels);
            buffer.AsSpan(0, count).CopyTo(samples);
            samples = samples[count..];
        }
    }

    /// <summary>Reload the plug-in to clear its history. Only call while audio is stopped.</summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (channels != 0) Initialize(sampleRate, channels);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        channels = 0;
        var previous = plugin;
        plugin = null;
        buffer = [];
        previous?.Dispose();
    }
}
