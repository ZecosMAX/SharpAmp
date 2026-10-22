using System.Runtime.InteropServices;
using System.Text;
using SharpAmp.Core.Dsp.Plugins;

namespace SharpAmp.Tests;

public sealed class Vst2PluginTests
{
    [Fact]
    public void EffectPrefixMatchesTheNativeAbiOnTheCurrentArchitecture()
    {
        Assert.Equal(IntPtr.Size == 8 ? 8 : 4, Marshal.OffsetOf<Vst2Native.Effect>(nameof(Vst2Native.Effect.Dispatcher)).ToInt32());
        Assert.Equal(IntPtr.Size == 8 ? 48 : 28, Marshal.OffsetOf<Vst2Native.Effect>(nameof(Vst2Native.Effect.InputCount)).ToInt32());
        Assert.Equal(IntPtr.Size == 8 ? 120 : 80, Marshal.OffsetOf<Vst2Native.Effect>(nameof(Vst2Native.Effect.ProcessReplacing)).ToInt32());
        Assert.Equal(88, Marshal.SizeOf<Vst2Native.TimeInfo>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HostsNativeEffectParametersPlanarAudioAndLifecycle(bool replacing)
    {
        using var native = new EffectFixture(replacing: replacing);
        using (var plugin = native.CreatePlugin())
        {
            Assert.Equal("Fixture gain", plugin.Name);
            Assert.Equal(2400, native.HostVersion);
            Assert.Equal(new VstParameter(0, "Gain", 0.5f), Assert.Single(plugin.Parameters));
            plugin.Initialize(48000, 2, 8);
            plugin.SetParameter(0, 0.25f);
            float[] samples = [1, 2, 3, 4, 99, 100];
            plugin.Process(samples, 2);
            Assert.Equal(new float[] { 0.25f, 0.5f, 0.75f, 1, 99, 100 }, samples);
            plugin.Process(samples, 2);
            Assert.Equal(new float[] { 0.0625f, 0.125f, 0.1875f, 0.25f, 99, 100 }, samples);
            Assert.Equal(48000, native.SampleRate);
            Assert.Equal(8, native.BlockSize);
            Assert.Equal(new double[] { 0, 2 }, native.SamplePositions);
            Assert.Equal(2, native.ProcessCalls);
        }
        Assert.Equal(new[] { "open", "sampleRate", "blockSize", "resume", "start", "stop", "suspend", "close", "unload" }, native.Lifecycle);
        Assert.True(native.ModuleReleased);
    }

    [Fact]
    public void MonoSessionFeedsBothStereoInputsAndAveragesTheOutputs()
    {
        using var native = new EffectFixture(secondOutputScale: 3);
        using var plugin = native.CreatePlugin();
        plugin.Initialize(44100, 1, 16);
        plugin.SetParameter(0, 1);
        float[] samples = [0.2f, -0.4f];
        plugin.Process(samples, 2);
        Assert.Equal(0.4f, samples[0], 6);
        Assert.Equal(-0.8f, samples[1], 6);
    }

    [Fact]
    public void StereoSessionDownmixesForMonoEffectAndDuplicatesItsOutput()
    {
        using var native = new EffectFixture(channels: 1);
        using var plugin = native.CreatePlugin();
        plugin.Initialize(48000, 2, 16);
        plugin.SetParameter(0, 1);
        float[] samples = [0.2f, 0.6f, -0.4f, -0.8f];
        plugin.Process(samples, 2);
        Assert.Equal(new float[] { 0.4f, 0.4f, -0.6f, -0.6f }, samples);
    }

    [Fact]
    public void ReinitializingStopsProcessingBeforeChangingTheFormat()
    {
        using var native = new EffectFixture();
        var plugin = native.CreatePlugin();
        plugin.Initialize(44100, 2, 8);
        plugin.Process([1, 1], 1);
        plugin.Initialize(96000, 1, 32);
        plugin.Process([1], 1);
        plugin.Dispose();
        plugin.Dispose();
        Assert.Equal(96000, native.SampleRate);
        Assert.Equal(32, native.BlockSize);
        Assert.Equal(new double[] { 0, 0 }, native.SamplePositions);
        Assert.Equal(2, native.Lifecycle.Count(x => x == "stop"));
        Assert.Equal(1, native.Lifecycle.Count(x => x == "close"));
        Assert.Equal(1, native.Lifecycle.Count(x => x == "unload"));
        Assert.Throws<ObjectDisposedException>(() => plugin.Process([1], 1));
        Assert.Throws<ObjectDisposedException>(() => plugin.SetParameter(0, 0.5f));
    }

    [Fact]
    public void InvalidParametersAndOversizedBlocksDoNotReachTheEffect()
    {
        using var native = new EffectFixture();
        using var plugin = native.CreatePlugin();
        Assert.Throws<InvalidOperationException>(() => plugin.Process([1, 2], 1));
        plugin.Initialize(48000, 2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => plugin.Process(new float[6], 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => plugin.Process(new float[2], 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => plugin.SetParameter(1, 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => plugin.SetParameter(0, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => plugin.SetParameter(0, 1.1f));
        Assert.Equal(0.5f, native.Gain);
        Assert.Equal(0, native.ProcessCalls);
    }

    [Fact]
    public void UnsupportedLayoutClosesTheEffectAndUnloadsTheModule()
    {
        using var native = new EffectFixture(channels: 4);
        Assert.Throws<NotSupportedException>(() => native.CreatePlugin());
        Assert.Equal(new[] { "open", "close", "unload" }, native.Lifecycle);
    }

    [Fact]
    public void MissingPluginHasAnActionableFileError()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll");
        var error = Assert.Throws<FileNotFoundException>(() => new Vst2Plugin(path));
        Assert.Equal(path, error.FileName);
    }

    [Fact]
    public void ProcessingDoesNotAllocateManagedMemory()
    {
        using var native = new EffectFixture(trackProcessing: false);
        using var plugin = native.CreatePlugin();
        plugin.Initialize(48000, 2, 16);
        var samples = new float[32];
        for (var i = 0; i < 100; i++) plugin.Process(samples, 16);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++) plugin.Process(samples, 16);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    // Cdecl reverse-P/Invoke thunks and unmanaged memory exercise exactly the pointers and
    // dispatcher ABI used for a DLL. The fixture retains delegates until after host disposal.
    private sealed class EffectFixture : IDisposable
    {
        private readonly Vst2Native.EntryPoint _entry;
        private readonly Vst2Native.Callback _dispatcher;
        private readonly Vst2Native.Process _process;
        private readonly Vst2Native.SetParameter _setParameter;
        private readonly Vst2Native.GetParameter _getParameter;
        private readonly nint _effect;
        private readonly int _channels;
        private readonly float _secondOutputScale;
        private Vst2Native.Callback? _host;
        private bool _active;
        private bool _started;
        public List<string> Lifecycle { get; } = [];
        public List<double> SamplePositions { get; } = [];
        public int HostVersion { get; private set; }
        public int SampleRate { get; private set; }
        public int BlockSize { get; private set; }
        public int ProcessCalls { get; private set; }
        public float Gain { get; private set; } = 0.5f;
        public bool ModuleReleased { get; private set; }

        public EffectFixture(int channels = 2, bool replacing = true, float secondOutputScale = 1, bool trackProcessing = true)
        {
            _channels = channels;
            _secondOutputScale = secondOutputScale;
            _entry = Open;
            _dispatcher = Dispatch;
            _process = trackProcessing ? Process : static (_, _, _, _) => { };
            _setParameter = (_, _, value) => Gain = value;
            _getParameter = (_, _) => Gain;
            _effect = Marshal.AllocHGlobal(Marshal.SizeOf<Vst2Native.Effect>());
            Marshal.StructureToPtr(new Vst2Native.Effect
            {
                Magic = Vst2Native.Magic,
                Dispatcher = Marshal.GetFunctionPointerForDelegate(_dispatcher),
                Process = Marshal.GetFunctionPointerForDelegate(_process),
                ProcessReplacing = replacing ? Marshal.GetFunctionPointerForDelegate(_process) : 0,
                SetParameter = Marshal.GetFunctionPointerForDelegate(_setParameter),
                GetParameter = Marshal.GetFunctionPointerForDelegate(_getParameter),
                ParameterCount = 1,
                ProgramCount = 1,
                InputCount = channels,
                OutputCount = channels,
                Flags = replacing ? 16 : 0
            }, _effect, false);
        }

        public Vst2Plugin CreatePlugin() => new(Marshal.GetFunctionPointerForDelegate(_entry), "Fixture.dll", () =>
        {
            Lifecycle.Add("unload");
            ModuleReleased = true;
        });

        private nint Open(Vst2Native.Callback host)
        {
            _host = host;
            HostVersion = (int)host(0, 1, 0, 0, 0, 0);
            return _effect;
        }

        private nint Dispatch(nint effect, int opcode, int index, nint value, nint pointer, float option)
        {
            switch (opcode)
            {
                case 0: Lifecycle.Add("open"); break;
                case 1:
                    Assert.False(_active);
                    Assert.False(_started);
                    Lifecycle.Add("close");
                    break;
                case 8: WriteString(pointer, "Gain"); return 1;
                case 10:
                    Assert.False(_active);
                    SampleRate = (int)option;
                    Assert.Equal(SampleRate, (int)_host!(effect, 16, 0, 0, 0, 0));
                    Lifecycle.Add("sampleRate");
                    break;
                case 11:
                    Assert.False(_active);
                    BlockSize = (int)value;
                    Assert.Equal(BlockSize, (int)_host!(effect, 17, 0, 0, 0, 0));
                    Lifecycle.Add("blockSize");
                    break;
                case 12:
                    _active = value != 0;
                    Lifecycle.Add(_active ? "resume" : "suspend");
                    break;
                case 45: WriteString(pointer, "Fixture gain"); return 1;
                case 71: _started = true; Lifecycle.Add("start"); break;
                case 72: _started = false; Lifecycle.Add("stop"); break;
            }
            return 0;
        }

        private void Process(nint effect, nint inputs, nint outputs, int frames)
        {
            Assert.True(_active && _started);
            Assert.InRange(frames, 1, BlockSize);
            Assert.Equal(2, (int)_host!(effect, 23, 0, 0, 0, 0));
            var time = Marshal.PtrToStructure<Vst2Native.TimeInfo>(_host(effect, 7, 0, 0, 0, 0));
            Assert.Equal(SampleRate, time.SampleRate);
            SamplePositions.Add(time.SamplePosition);
            for (var channel = 0; channel < _channels; channel++)
            {
                var input = new float[frames];
                var output = new float[frames];
                Marshal.Copy(Marshal.ReadIntPtr(inputs, channel * IntPtr.Size), input, 0, frames);
                var outputPointer = Marshal.ReadIntPtr(outputs, channel * IntPtr.Size);
                Marshal.Copy(outputPointer, output, 0, frames);
                for (var i = 0; i < frames; i++) output[i] += input[i] * Gain * (channel == 1 ? _secondOutputScale : 1);
                Marshal.Copy(output, 0, outputPointer, frames);
            }
            ProcessCalls++;
        }

        private static void WriteString(nint pointer, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value + '\0');
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(_effect);
            GC.KeepAlive(_entry);
            GC.KeepAlive(_dispatcher);
            GC.KeepAlive(_process);
            GC.KeepAlive(_setParameter);
            GC.KeepAlive(_getParameter);
        }
    }
}
