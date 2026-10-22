using System.Runtime.InteropServices;
using System.Text;

namespace SharpAmp.Core.Dsp.Plugins;

internal sealed class Vst2Plugin : IVstPlugin
{
    private readonly Vst2Native.Callback _hostCallback;
    private readonly Action _releaseModule;
    private Vst2Native.Callback? _dispatcher;
    private Vst2Native.Process? _process;
    private Vst2Native.SetParameter? _setParameter;
    private nint _effect;
    private nint _directory;
    private nint _timeInfo;
    private PlanarBuffers? _inputs;
    private PlanarBuffers? _outputs;
    private int _sampleRate = 44100;
    private int _maxBlockSize = 1024;
    private int _channels;
    private int _parameterCount;
    private double _samplePosition;
    private bool _active;
    private bool _processing;
    private bool _opened;
    private bool _disposed;

    public string Name { get; private set; }
    public int InputChannels { get; private set; }
    public int OutputChannels { get; private set; }
    public IReadOnlyList<VstParameter> Parameters { get; private set; } = Array.Empty<VstParameter>();

    public Vst2Plugin(string path) : this(LoadModule(path), path) { }

    private Vst2Plugin((nint Module, nint Entry) module, string path)
        : this(module.Entry, path, () => NativeLibrary.Free(module.Module)) { }

    // The entry-point seam also lets ABI tests exercise plug-ins without shipping third-party binaries.
    internal Vst2Plugin(nint entryPoint, string path, Action releaseModule)
    {
        _releaseModule = releaseModule;
        _hostCallback = HostCallback;
        Name = Path.GetFileNameWithoutExtension(path);
        try
        {
            _directory = Marshal.StringToHGlobalAnsi(Path.GetDirectoryName(Path.GetFullPath(path))!);
            _timeInfo = Marshal.AllocHGlobal(Marshal.SizeOf<Vst2Native.TimeInfo>());
            UpdateTimeInfo();
            _effect = Marshal.GetDelegateForFunctionPointer<Vst2Native.EntryPoint>(entryPoint)(_hostCallback);
            if (_effect == 0 || Marshal.ReadInt32(_effect) != Vst2Native.Magic)
                throw new InvalidOperationException($"'{path}' did not return a valid VST2 effect.");

            var effect = ReadEffect();
            if (effect.Dispatcher == 0)
                throw new InvalidOperationException("The VST2 effect does not provide a dispatcher.");
            _dispatcher = Marshal.GetDelegateForFunctionPointer<Vst2Native.Callback>(effect.Dispatcher);
            Dispatch(0); // effOpen
            _opened = true;
            effect = ReadEffect();
            InputChannels = effect.InputCount;
            OutputChannels = effect.OutputCount;
            _parameterCount = effect.ParameterCount;
            if (InputChannels is < 1 or > 2 || OutputChannels is < 1 or > 2 || (effect.Flags & 256) != 0)
                throw new NotSupportedException("VST2 effects must have one or two audio inputs and outputs; instruments and side-chain/surround layouts are not supported.");
            if (Dispatch(35) == 10) // effGetPlugCategory: shell
                throw new NotSupportedException("VST2 shell containers are not supported. Select an individual effect plug-in.");
            if (_parameterCount is < 0 or > 65536 || (_parameterCount > 0 && (effect.SetParameter == 0 || effect.GetParameter == 0)))
                throw new InvalidOperationException("The VST2 effect returned invalid parameter information.");

            var processPointer = (effect.Flags & 16) != 0 && effect.ProcessReplacing != 0
                ? effect.ProcessReplacing : effect.Process;
            if (processPointer == 0)
                throw new NotSupportedException("The VST2 effect does not support 32-bit floating-point audio processing.");
            _process = Marshal.GetDelegateForFunctionPointer<Vst2Native.Process>(processPointer);
            if (effect.SetParameter != 0)
                _setParameter = Marshal.GetDelegateForFunctionPointer<Vst2Native.SetParameter>(effect.SetParameter);
            var name = ReadString(45); // effGetEffectName
            if (!string.IsNullOrWhiteSpace(name)) Name = name;
            var getParameter = effect.GetParameter == 0 ? null : Marshal.GetDelegateForFunctionPointer<Vst2Native.GetParameter>(effect.GetParameter);
            var parameters = new VstParameter[_parameterCount];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterName = ReadString(8, i); // effGetParamName
                var defaultValue = getParameter!(_effect, i);
                if (!float.IsFinite(defaultValue)) defaultValue = 0;
                parameters[i] = new VstParameter((uint)i, string.IsNullOrWhiteSpace(parameterName) ? $"Parameter {i}" : parameterName, Math.Clamp(defaultValue, 0, 1));
            }
            Parameters = Array.AsReadOnly(parameters);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Initialize(int sampleRate, int channels, int maxBlockSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBlockSize);
        if (channels is < 1 or > 2) throw new NotSupportedException("VST effects support mono and stereo audio sessions.");
        Suspend();
        _inputs?.Dispose();
        _outputs?.Dispose();
        _inputs = null;
        _outputs = null;
        _sampleRate = sampleRate;
        _maxBlockSize = maxBlockSize;
        _channels = channels;
        _samplePosition = 0;
        UpdateTimeInfo();
        Dispatch(10, option: sampleRate); // effSetSampleRate
        Dispatch(11, value: maxBlockSize); // effSetBlockSize
        _inputs = new PlanarBuffers(InputChannels, maxBlockSize);
        _outputs = new PlanarBuffers(OutputChannels, maxBlockSize);
        Dispatch(12, value: 1); // effMainsChanged: resume
        _active = true;
        Dispatch(71); // effStartProcess
    }

    public void Process(float[] interleaved, int frameCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(interleaved);
        if (!_active) throw new InvalidOperationException("Initialize the VST2 effect before processing audio.");
        if (frameCount < 0 || frameCount > _maxBlockSize || frameCount > interleaved.Length / _channels)
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        if (frameCount == 0) return;
        var effect = ReadEffect();
        if (effect.InputCount != InputChannels || effect.OutputCount != OutputChannels)
            throw new NotSupportedException("The VST2 effect changed its audio channel layout while running.");
        for (var channel = 0; channel < InputChannels; channel++)
        {
            var input = _inputs!.Samples[channel];
            for (var frame = 0; frame < frameCount; frame++)
                input[frame] = _channels == 2 && InputChannels == 1
                    ? (interleaved[frame * 2] + interleaved[frame * 2 + 1]) * 0.5f
                    : interleaved[frame * _channels + (_channels == 1 ? 0 : channel)];
        }
        foreach (var output in _outputs!.Samples) Array.Clear(output, 0, frameCount);
        UpdateTimeInfo();
        _processing = true;
        try { _process!(_effect, _inputs!.Pointers, _outputs.Pointers, frameCount); }
        finally { _processing = false; }
        for (var frame = 0; frame < frameCount; frame++)
            for (var channel = 0; channel < _channels; channel++)
                interleaved[frame * _channels + channel] = _channels == 1 && OutputChannels == 2
                    ? (_outputs.Samples[0][frame] + _outputs.Samples[1][frame]) * 0.5f
                    : _outputs.Samples[OutputChannels == 1 ? 0 : channel][frame];
        _samplePosition += frameCount;
    }

    public void SetParameter(uint id, float value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (id >= _parameterCount) throw new ArgumentOutOfRangeException(nameof(id), "VST2 parameter IDs are zero-based indices.");
        if (!float.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(value), "VST parameter values must be between 0 and 1.");
        _setParameter!(_effect, (int)id, value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            Suspend();
            if (_opened) Dispatch(1); // effClose destroys the effect.
        }
        finally
        {
            _disposed = true;
            _effect = 0;
            _inputs?.Dispose();
            _outputs?.Dispose();
            if (_directory != 0) Marshal.FreeHGlobal(_directory);
            if (_timeInfo != 0) Marshal.FreeHGlobal(_timeInfo);
            _releaseModule();
            GC.KeepAlive(_hostCallback);
        }
    }

    private void Suspend()
    {
        if (!_active) return;
        Dispatch(72); // effStopProcess
        Dispatch(12); // effMainsChanged: suspend
        _active = false;
    }

    private nint Dispatch(int opcode, int index = 0, nint value = 0, nint pointer = 0, float option = 0)
        => _dispatcher!(_effect, opcode, index, value, pointer, option);

    private string ReadString(int opcode, int index = 0)
    {
        var bytes = new byte[256];
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { Dispatch(opcode, index, pointer: pin.AddrOfPinnedObject()); }
        finally { pin.Free(); }
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end).Trim();
    }

    private nint HostCallback(nint effect, int opcode, int index, nint value, nint pointer, float option)
    {
        // Never propagate a managed exception through a native callback.
        try
        {
            return opcode switch
            {
                1 => 2400, // audioMasterVersion
                4 => index >= 0 && index < (value == 0 ? InputChannels : OutputChannels) ? 0 : 1,
                7 => _timeInfo,
                16 => _sampleRate,
                17 => _maxBlockSize,
                22 => 1, // replacing
                23 => _processing ? 2 : 1, // realtime audio / user thread
                32 or 33 => WriteHostName(pointer),
                34 => 1000,
                37 => Marshal.PtrToStringAnsi(pointer) == "sendVstTimeInfo" ? 1 : 0,
                38 => 1, // English
                41 => _directory,
                _ => 0
            };
        }
        catch { return 0; }
    }

    private static nint WriteHostName(nint pointer)
    {
        if (pointer == 0) return 0;
        ReadOnlySpan<byte> name = "SharpAmp\0"u8;
        for (var i = 0; i < name.Length; i++) Marshal.WriteByte(pointer, i, name[i]);
        return 1;
    }

    private unsafe Vst2Native.Effect ReadEffect() => *(Vst2Native.Effect*)_effect;

    private unsafe void UpdateTimeInfo()
    {
        var time = new Vst2Native.TimeInfo
        {
            SamplePosition = _samplePosition,
            SampleRate = _sampleRate,
            QuarterNotePosition = _samplePosition * 2 / _sampleRate,
            Tempo = 120,
            TimeSignatureNumerator = 4,
            TimeSignatureDenominator = 4,
            Flags = 2 | 512 | 1024 | 8192 // playing, quarter-note position, tempo, time signature
        };
        // Direct struct assignment avoids Marshal.StructureToPtr boxing on the audio thread.
        *(Vst2Native.TimeInfo*)_timeInfo = time;
    }

    private static (nint Module, nint Entry) LoadModule(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("VST2 hosting currently supports Windows plug-ins.");
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("The VST2 plug-in file was not found.", path);
        nint module;
        try
        {
            module = NativeLibrary.Load(path, typeof(Vst2Plugin).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories);
        }
        catch (BadImageFormatException exception)
        {
            throw new BadImageFormatException($"Cannot load '{path}'. Use a Windows VST2 plug-in matching the application's {RuntimeInformation.ProcessArchitecture} architecture.", exception);
        }
        catch (DllNotFoundException exception)
        {
            throw new DllNotFoundException($"Cannot load VST2 plug-in '{path}'. Check its required native dependencies and {RuntimeInformation.ProcessArchitecture} architecture.", exception);
        }
        if (NativeLibrary.TryGetExport(module, "VSTPluginMain", out var entry) || NativeLibrary.TryGetExport(module, "main", out entry))
            return (module, entry);
        NativeLibrary.Free(module);
        throw new InvalidOperationException($"'{path}' is not a VST2 plug-in (VSTPluginMain/main export missing).");
    }

    private sealed class PlanarBuffers : IDisposable
    {
        private readonly GCHandle[] _pins;
        private GCHandle _pointerPin;
        public float[][] Samples { get; }
        public nint Pointers => _pointerPin.AddrOfPinnedObject();

        public PlanarBuffers(int channels, int frames)
        {
            Samples = new float[channels][];
            _pins = new GCHandle[channels];
            var pointers = new nint[channels];
            try
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    Samples[channel] = new float[frames];
                    _pins[channel] = GCHandle.Alloc(Samples[channel], GCHandleType.Pinned);
                    pointers[channel] = _pins[channel].AddrOfPinnedObject();
                }
                _pointerPin = GCHandle.Alloc(pointers, GCHandleType.Pinned);
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            for (var i = 0; i < _pins.Length; i++)
                if (_pins[i].IsAllocated) _pins[i].Free();
            if (_pointerPin.IsAllocated) _pointerPin.Free();
        }
    }
}
