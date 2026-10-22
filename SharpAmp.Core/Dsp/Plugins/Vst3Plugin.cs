using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace SharpAmp.Core.Dsp.Plugins;

internal sealed class Vst3Plugin(string path) : IVstPlugin
{
    private static readonly int FrameCountOffset = (int)Marshal.OffsetOf<Vst3Native.ProcessData>(nameof(Vst3Native.ProcessData.NumSamples));
    private static readonly int SilenceFlagsOffset = (int)Marshal.OffsetOf<Vst3Native.AudioBusBuffers>(nameof(Vst3Native.AudioBusBuffers.SilenceFlags));
    private int pluginId = -1;
    private Vst3Native? native;
    private readonly List<GCHandle> pinnedBuffers = [];
    private IntPtr processData;
    private NativeBus[] inputs = [];
    private NativeBus[] outputs = [];
    private int mainInput;
    private int mainOutput;
    private int hostChannels;
    private int maxBlockSize;
    private bool processing;
    private bool disposed;
    private readonly HashSet<uint> parameterIds = [];

    public string Name { get; } = Path.GetFileNameWithoutExtension(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    public int InputChannels { get; private set; }
    public int OutputChannels { get; private set; }
    public IReadOnlyList<VstParameter> Parameters { get; private set; } = [];

    public void Initialize(int sampleRate, int channels, int maxBlockSize)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (pluginId >= 0) throw new InvalidOperationException("The VST3 instance is already initialized.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBlockSize);
        if (channels is not 1 and not 2) throw new NotSupportedException("VST3 effects support mono or stereo sessions.");
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("VST3 hosting requires a Windows x64 process and a 64-bit plug-in.");

        var modulePath = ResolveModulePath(path);
        using (var stream = File.OpenRead(modulePath))
        using (var reader = new PEReader(stream))
            if (reader.PEHeaders.CoffHeader.Machine != Machine.Amd64)
                throw new BadImageFormatException("The VST3 plug-in must be a Windows x64 binary.", modulePath);

        hostChannels = channels;
        this.maxBlockSize = maxBlockSize;
        try
        {
            native = new Vst3Native();
            var hasEditor = 0;
            pluginId = native!.LoadVst3Ext(modulePath, ref hasEditor);
            if (pluginId < 0) throw new InvalidOperationException($"Could not load VST3 plug-in '{path}'.");

            mainInput = FindMainBus(0);
            mainOutput = FindMainBus(1);
            Check(native!.GetBusInfo(pluginId, 0, 0, mainInput, out var inputInfo), "read input bus");
            Check(native!.GetBusInfo(pluginId, 0, 1, mainOutput, out var outputInfo), "read output bus");
            InputChannels = inputInfo.ChannelCount;
            OutputChannels = outputInfo.ChannelCount;
            if (InputChannels is not 1 and not 2 || OutputChannels is not 1 and not 2)
                throw new NotSupportedException("VST3 effects require a mono or stereo main input and output bus.");

            var count = native!.GetParameterCount(pluginId);
            if (count < 0 || count > 65536) throw new InvalidOperationException("The plug-in returned an invalid parameter count.");
            var parameters = new List<VstParameter>(count);
            for (var index = 0; index < count; index++)
            {
                Check(native!.GetParameterInfo(pluginId, index, out var info), "read parameter metadata");
                // Read-only meters and hidden parameters cannot be controlled by the host.
                if ((info.Flags & ((1 << 1) | (1 << 4))) != 0) continue;
                var value = native!.GetParameterNormalized(pluginId, info.Id);
                if (!double.IsFinite(value)) value = info.DefaultNormalizedValue;
                parameters.Add(new(info.Id, info.Title, (float)Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1)));
                parameterIds.Add(info.Id);
            }
            Parameters = parameters.AsReadOnly();

            Check(native!.SetupAudioProcessor(pluginId, maxBlockSize, ref processData, count, sampleRate), "set up audio processing");
            if (processData == IntPtr.Zero) throw new InvalidOperationException("The VST3 bridge did not return process data.");
            var data = Marshal.PtrToStructure<Vst3Native.ProcessData>(processData);
            if (data.SymbolicSampleSize != 0) throw new NotSupportedException("The VST3 plug-in does not support 32-bit float processing.");
            inputs = CreateBuses(data.Inputs, data.NumInputs);
            outputs = CreateBuses(data.Outputs, data.NumOutputs);
            if (mainInput >= inputs.Length || mainOutput >= outputs.Length ||
                inputs[mainInput].Channels.Length != InputChannels || outputs[mainOutput].Channels.Length != OutputChannels)
                throw new InvalidOperationException("The plug-in changed its audio bus layout during initialization.");

            ActivateBuses(inputs.Length, 0, mainInput);
            ActivateBuses(outputs.Length, 1, mainOutput);
            var startResult = native!.SetProcessing(pluginId, 1);
            // The SDK AudioEffect base class leaves this optional notification
            // unimplemented. Such effects can still implement process normally.
            if (startResult != unchecked((int)0x80004001)) Check(startResult, "start processing");
            processing = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal static string ResolveModulePath(string pluginPath)
    {
        var fullPath = Path.GetFullPath(pluginPath);
        if (File.Exists(fullPath)) return fullPath;
        if (!Directory.Exists(fullPath)) throw new FileNotFoundException("VST3 plug-in was not found.", fullPath);
        var binaries = Path.Combine(fullPath, "Contents", "x86_64-win");
        if (!Directory.Exists(binaries)) throw new FileNotFoundException("The VST3 bundle has no Windows x64 binaries.", binaries);
        var expected = Path.Combine(binaries, Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        if (File.Exists(expected)) return expected;
        var candidates = Directory.GetFiles(binaries, "*.vst3");
        if (candidates.Length == 1) return candidates[0];
        throw new FileNotFoundException("The VST3 bundle must contain one identifiable Windows x64 module.", binaries);
    }

    private int FindMainBus(int direction)
    {
        var count = native!.GetBusCount(pluginId, 0, direction);
        if (count < 0 || count > 128) throw new InvalidOperationException("The plug-in returned an invalid audio bus count.");
        var selected = -1;
        for (var index = 0; index < count; index++)
        {
            Check(native!.GetBusInfo(pluginId, 0, direction, index, out var info), "read audio bus");
            if (info.BusType != 0) continue;
            if (selected >= 0) throw new NotSupportedException("VST3 effects with multiple main audio buses are not supported.");
            selected = index;
        }
        if (selected < 0) throw new NotSupportedException("A VST3 audio effect must have a main audio input and output; instruments are not supported.");
        return selected;
    }

    private void ActivateBuses(int count, int direction, int main)
    {
        for (var index = 0; index < count; index++)
            Check(native!.ActivateBus(pluginId, 0, direction, index, (byte)(index == main ? 1 : 0)), "activate audio buses");
    }

    private NativeBus[] CreateBuses(IntPtr address, int count)
    {
        if (count < 0 || count > 128 || (count > 0 && address == IntPtr.Zero))
            throw new InvalidOperationException("The VST3 bridge returned invalid audio buffers.");
        var result = new NativeBus[count];
        for (var bus = 0; bus < count; bus++)
        {
            var busAddress = IntPtr.Add(address, bus * Marshal.SizeOf<Vst3Native.AudioBusBuffers>());
            var data = Marshal.PtrToStructure<Vst3Native.AudioBusBuffers>(busAddress);
            if (data.NumChannels < 0 || data.NumChannels > 64 || (data.NumChannels > 0 && data.Channels == IntPtr.Zero))
                throw new InvalidOperationException("The VST3 bridge returned an invalid channel layout.");
            var channelPointers = new IntPtr[data.NumChannels];
            var buffers = new float[data.NumChannels][];
            for (var channel = 0; channel < data.NumChannels; channel++)
            {
                buffers[channel] = new float[maxBlockSize];
                var pin = GCHandle.Alloc(buffers[channel], GCHandleType.Pinned);
                pinnedBuffers.Add(pin);
                channelPointers[channel] = pin.AddrOfPinnedObject();
                Marshal.WriteIntPtr(data.Channels, channel * IntPtr.Size, channelPointers[channel]);
            }
            result[bus] = new(busAddress, channelPointers, buffers);
        }
        return result;
    }

    public void Process(float[] interleaved, int frameCount)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!processing) throw new InvalidOperationException("Initialize the VST3 plug-in before processing.");
        if (frameCount < 0 || frameCount > maxBlockSize || (long)frameCount * hostChannels > interleaved.Length)
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        if (frameCount == 0) return;

        foreach (var bus in inputs)
        {
            Marshal.WriteInt64(bus.Address, SilenceFlagsOffset, 0);
            foreach (var buffer in bus.Buffers) Array.Clear(buffer, 0, frameCount);
        }
        var input = inputs[mainInput];
        for (var frame = 0; frame < frameCount; frame++)
        {
            if (InputChannels == 1)
                input.Buffers[0][frame] = hostChannels == 1 ? interleaved[frame] : (interleaved[2 * frame] + interleaved[2 * frame + 1]) * 0.5f;
            else
            {
                input.Buffers[0][frame] = interleaved[frame * hostChannels];
                input.Buffers[1][frame] = interleaved[frame * hostChannels + (hostChannels == 2 ? 1 : 0)];
            }
        }
        foreach (var bus in outputs)
        {
            Marshal.WriteInt64(bus.Address, SilenceFlagsOffset, 0);
            for (var channel = 0; channel < bus.Channels.Length; channel++)
            {
                Array.Clear(bus.Buffers[channel], 0, frameCount);
            }
        }

        Marshal.WriteInt32(processData, FrameCountOffset, frameCount);
        Check(native!.Process(pluginId), "process audio");
        var output = outputs[mainOutput];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var left = output.Buffers[0][frame];
            var right = OutputChannels == 2 ? output.Buffers[1][frame] : left;
            if (hostChannels == 1) interleaved[frame] = (left + right) * 0.5f;
            else
            {
                interleaved[2 * frame] = left;
                interleaved[2 * frame + 1] = right;
            }
        }
    }

    public void SetParameter(uint id, float value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!processing) throw new InvalidOperationException("Initialize the VST3 plug-in before setting parameters.");
        if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
        if (!parameterIds.Contains(id)) throw new ArgumentException($"VST3 plug-in '{Name}' has no writable parameter with ID {id}.", nameof(id));
        Check(native!.SetParameterNormalized(pluginId, id, value), "set controller parameter");
        Check(native!.AddParameterData(pluginId, id, value), "queue processor parameter");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            try
            {
                if (processing) native!.SetProcessing(pluginId, 0);
            }
            finally
            {
                if (pluginId >= 0) native!.DisposePlugin(pluginId);
            }
        }
        finally
        {
            foreach (var pin in pinnedBuffers) pin.Free();
            pinnedBuffers.Clear();
            native?.Dispose();
            native = null;
            pluginId = -1;
            processData = IntPtr.Zero;
            processing = false;
            inputs = [];
            outputs = [];
        }
    }

    private void Check(int result, string operation)
    {
        if (result != 0) throw new InvalidOperationException($"VST3 plug-in '{Name}' failed to {operation} (result {result}).");
    }

    private sealed record NativeBus(IntPtr Address, IntPtr[] Channels, float[][] Buffers);
}

