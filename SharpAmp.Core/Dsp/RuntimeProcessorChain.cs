using System.Globalization;
using SharpAmp.Core.Dsp.Configuration;

namespace SharpAmp.Core.Dsp;

/// <summary>
/// A serial effect chain editable from a control thread while the audio thread processes samples.
/// Edits build fresh instances and use LiveProcessorChain's crossfade; effect history resets.
/// Owns its processors. Dispose after the AudioSession has been disposed.
/// </summary>
public sealed class RuntimeProcessorChain : IDigitalSignalProcessor, IDisposable
{
    private readonly object gate = new();
    private LiveProcessorChain live = new(new ProcessorChain());
    private List<ProcessorConfiguration> configurations = [];
    private bool disposed;
    private bool initialized;

    public bool IsBypassed { get => live.IsBypassed; set => live.IsBypassed = value; }

    /// <summary>Independent snapshots of the configured effects in processing order.</summary>
    public IReadOnlyList<ProcessorConfiguration> Configurations
    {
        get { lock (gate) return configurations.Select(Clone).ToArray(); }
    }

    /// <summary>Add an effect and return its current index. Initialization failures leave the chain unchanged.</summary>
    public int Add(ProcessorConfiguration effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        lock (gate)
        {
            var next = configurations.Append(Clone(effect)).ToList();
            Publish(next);
            return next.Count - 1;
        }
    }

    public int AddVst(string pluginPath, IReadOnlyDictionary<uint, float>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        return Add(new ProcessorConfiguration
        {
            Type = pluginPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ? "vst3" : "vst2",
            PluginPath = pluginPath,
            Parameters = parameters?.ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture), pair => pair.Value) ?? []
        });
    }

    public void RemoveAt(int index)
    {
        lock (gate)
        {
            var next = configurations.ToList();
            next.RemoveAt(index);
            Publish(next);
        }
    }

    public void ReplaceAt(int index, ProcessorConfiguration effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        lock (gate)
        {
            var next = configurations.ToList();
            next[index] = Clone(effect);
            Publish(next);
        }
    }

    public void Clear() { lock (gate) Publish([]); }

    private void Publish(List<ProcessorConfiguration> next)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var chain = ProcessorChain.FromConfiguration(new ProcessorChainConfiguration { Processors = next });
        if (initialized) live.Replace(chain);
        else
        {
            var replacement = new LiveProcessorChain(chain, live.IsBypassed);
            try { live.Dispose(); }
            catch
            {
                try { replacement.Dispose(); } catch { /* Preserve the original cleanup error. */ }
                throw;
            }
            live = replacement;
        }
        configurations = next;
    }

    private static ProcessorConfiguration Clone(ProcessorConfiguration effect) => new()
    {
        Type = effect.Type,
        PluginPath = effect.PluginPath,
        Parameters = effect.Parameters is null ? throw new ArgumentException("parameters must be an object.")
            : new(effect.Parameters, StringComparer.Ordinal)
    };

    /// <summary>Initialize only while the audio stream is stopped.</summary>
    public void Initialize(int sampleRate, int channels)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            initialized = false;
            live.Initialize(sampleRate, channels);
            initialized = true;
        }
    }
    public void Process(Span<float> samples) => live.Process(samples);
    /// <summary>Reset effect history only while the audio stream is stopped.</summary>
    public void Reset() { lock (gate) live.Reset(); }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            live.Dispose();
        }
    }
}
