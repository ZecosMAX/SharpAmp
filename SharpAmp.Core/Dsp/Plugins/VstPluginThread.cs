using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace SharpAmp.Core.Dsp.Plugins;

/// <summary>
/// Owns the plug-in's STA control thread and Windows message loop. Process stays on the
/// caller's audio thread; the caller must stop processing before control changes/disposal.
/// </summary>
internal sealed class VstPluginThread : IVstPlugin
{
    private readonly object _controlGate = new();
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private IVstPlugin? _plugin;
    private Metadata _metadata = new("", 0, 0, Array.Empty<VstParameter>());
    private ExceptionDispatchInfo? _threadFailure;
    private bool _stopping;
    private bool _disposed;

    public string Name => Volatile.Read(ref _metadata).Name;
    public int InputChannels => Volatile.Read(ref _metadata).Inputs;
    public int OutputChannels => Volatile.Read(ref _metadata).Outputs;
    public IReadOnlyList<VstParameter> Parameters => Volatile.Read(ref _metadata).Parameters;

    public VstPluginThread(Func<IVstPlugin> createPlugin)
    {
        ArgumentNullException.ThrowIfNull(createPlugin);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native VST hosting requires Windows.");
        _thread = new Thread(Run) { IsBackground = true, Name = "SharpAmp VST control" };
        _thread.SetApartmentState(ApartmentState.STA);
        try { _thread.Start(); }
        catch { _wake.Dispose(); throw; }
        try
        {
            Invoke(() =>
            {
                _plugin = createPlugin() ?? throw new InvalidOperationException("The VST factory returned no plug-in.");
                CaptureMetadata();
            });
        }
        catch
        {
            try { Dispose(); } catch { /* Preserve the construction error after joining the owner. */ }
            throw;
        }
    }

    public void Initialize(int sampleRate, int channels, int maxBlockSize)
    {
        lock (_controlGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Invoke(() =>
            {
                _plugin!.Initialize(sampleRate, channels, maxBlockSize);
                CaptureMetadata();
            });
        }
    }

    public void SetParameter(uint id, float value)
    {
        lock (_controlGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Invoke(() => _plugin!.SetParameter(id, value));
        }
    }

    public void Process(float[] interleaved, int frameCount)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        _threadFailure?.Throw();
        _plugin!.Process(interleaved, frameCount);
    }

    public void Dispose()
    {
        lock (_controlGate)
        {
            if (_disposed) return;
            Volatile.Write(ref _disposed, true);
            try
            {
                Invoke(() =>
                {
                    try { _plugin?.Dispose(); }
                    finally
                    {
                        _plugin = null;
                        _stopping = true;
                    }
                });
            }
            finally
            {
                _thread.Join();
                _wake.Dispose();
            }
        }
    }

    private void CaptureMetadata()
    {
        var plugin = _plugin!;
        Volatile.Write(ref _metadata, new Metadata(plugin.Name, plugin.InputChannels, plugin.OutputChannels,
            Array.AsReadOnly(plugin.Parameters.ToArray())));
    }

    private void Invoke(Action action)
    {
        var command = new Command(action);
        _commands.Enqueue(command);
        _wake.Set();
        // Also observe termination so a failed message loop cannot strand a caller forever.
        Task.WaitAny(command.Completion.Task, _exited.Task);
        if (!command.Completion.Task.IsCompleted)
        {
            _threadFailure?.Throw();
            throw new InvalidOperationException("The VST control thread stopped unexpectedly.");
        }
        command.Completion.Task.GetAwaiter().GetResult();
    }

    private void Run()
    {
        var oleInitialized = false;
        try
        {
            Marshal.ThrowExceptionForHR(OleInitialize(0));
            oleInitialized = true;
            nint[] handles = [_wake.SafeWaitHandle.DangerousGetHandle()];
            while (!_stopping)
            {
                while (!_stopping && _commands.TryDequeue(out var command)) command.Execute();
                if (_stopping) break;
                // A bounded drain lets control commands make progress even with busy plug-in timers.
                for (var i = 0; i < 100 && PeekMessage(out var message, 0, 0, 0, 1); i++)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
                // QS_ALLINPUT + MWMO_INPUTAVAILABLE also dispatches timers for hidden plug-in windows.
                if (MsgWaitForMultipleObjectsEx(1, handles, uint.MaxValue, 0x04FF, 0x0004) == uint.MaxValue)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch (Exception error) { _threadFailure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            try { _plugin?.Dispose(); }
            catch (Exception error) { _threadFailure ??= ExceptionDispatchInfo.Capture(error); }
            if (oleInitialized) OleUninitialize();
            _exited.TrySetResult();
        }
    }

    private sealed record Metadata(string Name, int Inputs, int Outputs, IReadOnlyList<VstParameter> Parameters);

    private sealed class Command(Action action)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Execute()
        {
            try { action(); Completion.TrySetResult(); }
            catch (Exception error) { Completion.TrySetException(error); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int OleInitialize(nint reserved);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void OleUninitialize();

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern uint MsgWaitForMultipleObjectsEx(uint count, [In] nint[] handles, uint milliseconds, uint wakeMask, uint flags);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", ExactSpelling = true)]
    private static extern nint DispatchMessage(ref Message message);
}
