using System.Runtime.InteropServices;
using SharpAmp.Core.Dsp.Plugins;

namespace SharpAmp.Tests;

public sealed class VstPluginThreadTests
{
    [Fact]
    public async Task NativeLifecycleUsesOneStaOwnerEvenWhenCallersChange()
    {
        Probe? backend = null;
        var caller = Environment.CurrentManagedThreadId;
        var plugin = new VstPluginThread(() => backend = new Probe());
        try
        {
            Assert.NotEqual(caller, backend!.Owner.ManagedThreadId);
            Assert.Equal(ApartmentState.STA, backend.Apartment);
            plugin.Initialize(48000, 2, 128);
            await Task.Run(() => plugin.SetParameter(0, 0.75f));
            Assert.Equal("Thread probe", plugin.Name);
            Assert.Equal(2, plugin.InputChannels);
            Assert.Equal(2, plugin.OutputChannels);
            Assert.Equal(new VstParameter(0, "Gain", 0.5f), Assert.Single(plugin.Parameters));
            var audioCaller = Environment.CurrentManagedThreadId;
            float[] samples = [1, 2];
            plugin.Process(samples, 1);
            Assert.Equal(audioCaller, backend.ProcessThread);
            Assert.Equal(new float[] { 0.75f, 1.5f }, samples);
        }
        finally { await Task.Run(plugin.Dispose); }
        Assert.Equal(new[] { "create", "initialize", "parameter", "dispose" }, backend!.Lifecycle);
        Assert.False(backend.Owner.IsAlive);
        Assert.Throws<ObjectDisposedException>(() => plugin.Process([1], 1));
        plugin.Dispose();
    }

    [Fact]
    public void ControlFailurePreservesTheExceptionAndStillDisposesOnTheOwner()
    {
        Probe? backend = null;
        var failure = new InvalidOperationException("Initialization failed");
        var plugin = new VstPluginThread(() => backend = new Probe { InitializeFailure = failure });
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => plugin.Initialize(48000, 2, 64)));
        plugin.Dispose();
        Assert.Equal(new[] { "create", "initialize", "dispose" }, backend!.Lifecycle);
        Assert.False(backend.Owner.IsAlive);
    }

    [Fact]
    public void FactoryFailureDoesNotLeaveAnOwnerThreadRunning()
    {
        Thread? owner = null;
        var failure = new InvalidOperationException("Load failed");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => new VstPluginThread(() =>
        {
            owner = Thread.CurrentThread;
            throw failure;
        })));
        Assert.NotNull(owner);
        Assert.False(owner.IsAlive);
    }

    [Fact]
    public void DisposalFailureStillJoinsTheOwnerThread()
    {
        Probe? backend = null;
        var failure = new InvalidOperationException("Close failed");
        var plugin = new VstPluginThread(() => backend = new Probe { DisposeFailure = failure });
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(plugin.Dispose));
        Assert.False(backend!.Owner.IsAlive);
        Assert.Equal(1, backend.Lifecycle.Count(x => x == "dispose"));
        plugin.Dispose();
    }

    [Fact]
    public void WindowsTimersRunOnTheOwnerWithoutFurtherControlCalls()
    {
        using var ticked = new ManualResetEventSlim();
        TimerProbe? backend = null;
        using (var plugin = new VstPluginThread(() => backend = new TimerProbe(ticked)))
        {
            Assert.True(ticked.Wait(TimeSpan.FromSeconds(5)), "The plug-in owner must pump Windows timer messages.");
            Assert.Equal(backend!.Owner.ManagedThreadId, backend.TimerThread);
        }
        Assert.False(backend!.Owner.IsAlive);
    }

    [Fact]
    public void AudioProcessingRunsWithoutManagedAllocations()
    {
        using var plugin = new VstPluginThread(() => new Probe());
        plugin.Initialize(48000, 2, 32);
        var samples = new float[64];
        for (var i = 0; i < 100; i++) plugin.Process(samples, 32);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++) plugin.Process(samples, 32);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    private class Probe : IVstPlugin
    {
        private float _gain = 0.5f;
        public Thread Owner { get; } = Thread.CurrentThread;
        public ApartmentState Apartment { get; } = Thread.CurrentThread.GetApartmentState();
        public List<string> Lifecycle { get; } = ["create"];
        public int ProcessThread { get; private set; }
        public Exception? InitializeFailure { get; init; }
        public Exception? DisposeFailure { get; init; }
        public string Name { get { VerifyOwner(); return "Thread probe"; } }
        public int InputChannels { get { VerifyOwner(); return 2; } }
        public int OutputChannels { get { VerifyOwner(); return 2; } }
        public IReadOnlyList<VstParameter> Parameters { get { VerifyOwner(); return [new(0, "Gain", 0.5f)]; } }

        public void Initialize(int sampleRate, int channels, int maxBlockSize)
        {
            VerifyOwner();
            Lifecycle.Add("initialize");
            if (InitializeFailure is not null) throw InitializeFailure;
        }

        public void SetParameter(uint id, float value)
        {
            VerifyOwner();
            Lifecycle.Add("parameter");
            _gain = value;
        }

        public void Process(float[] interleaved, int frameCount)
        {
            ProcessThread = Environment.CurrentManagedThreadId;
            for (var i = 0; i < frameCount * 2; i++) interleaved[i] *= _gain;
        }

        public virtual void Dispose()
        {
            VerifyOwner();
            Lifecycle.Add("dispose");
            if (DisposeFailure is not null) throw DisposeFailure;
        }

        protected void VerifyOwner() => Assert.Same(Owner, Thread.CurrentThread);
    }

    private sealed class TimerProbe : Probe
    {
        private readonly TimerCallback _callback;
        private readonly nuint _timer;
        public int TimerThread { get; private set; }

        public TimerProbe(ManualResetEventSlim ticked)
        {
            _callback = (_, _, _, _) =>
            {
                TimerThread = Environment.CurrentManagedThreadId;
                ticked.Set();
            };
            _timer = SetTimer(0, 0, 10, _callback);
            Assert.NotEqual((nuint)0, _timer);
        }

        public override void Dispose()
        {
            VerifyOwner();
            KillTimer(0, _timer);
            GC.KeepAlive(_callback);
            base.Dispose();
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void TimerCallback(nint window, uint message, nuint timer, uint time);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern nuint SetTimer(nint window, nuint timer, uint milliseconds, TimerCallback callback);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool KillTimer(nint window, nuint timer);
    }
}
