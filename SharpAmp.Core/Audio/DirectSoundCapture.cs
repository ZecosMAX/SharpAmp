using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace SharpAmp.Core.Audio;

/// <summary>NAudio has no DirectSound input class. This adapter exposes native capture as IWaveIn.</summary>
internal sealed class DirectSoundCapture : IWaveIn
{
    private readonly NativeCapture capture;
    private readonly NativeBuffer buffer;
    private readonly WaveFormat format;
    private readonly byte[] bytes;
    private readonly int pollMilliseconds;
    private readonly ManualResetEvent stop = new(false);
    private Thread? worker;
    private bool disposed;
    public WaveFormat WaveFormat { get => format; set => throw new NotSupportedException("The capture format is fixed at construction."); }
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public static IReadOnlyList<AudioDevice> GetDevices()
    {
        List<AudioDevice> devices = [];
        EnumerateCallback callback = (id, description, _, _) =>
        {
            var guid = id == IntPtr.Zero ? Guid.Empty : Marshal.PtrToStructure<Guid>(id);
            devices.Add(new(AudioBackend.DirectSound, AudioDirection.Input, guid.ToString(), Marshal.PtrToStringUni(description) ?? "DirectSound input"));
            return true;
        };
        Marshal.ThrowExceptionForHR(DirectSoundCaptureEnumerate(callback, IntPtr.Zero));
        GC.KeepAlive(callback);
        return devices;
    }

    public DirectSoundCapture(Guid device, WaveFormat format, int latencyMilliseconds)
    {
        this.format = format;
        pollMilliseconds = Math.Clamp(latencyMilliseconds / 2, 1, 10);
        var frames = checked(format.SampleRate * Math.Max(1000, latencyMilliseconds * 4) / 1000);
        bytes = new byte[checked(frames * format.BlockAlign)];
        var formatPointer = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(DirectSoundCaptureCreate(ref device, out capture, IntPtr.Zero));
            formatPointer = WaveFormat.MarshalToPtr(format);
            var description = new CaptureBufferDescription
            {
                Size = Marshal.SizeOf<CaptureBufferDescription>(),
                BufferBytes = bytes.Length,
                Format = formatPointer
            };
            Marshal.ThrowExceptionForHR(capture.CreateCaptureBuffer(ref description, out buffer, IntPtr.Zero));
        }
        catch
        {
            if (capture is not null) Marshal.ReleaseComObject(capture);
            stop.Dispose();
            throw;
        }
        finally { if (formatPointer != IntPtr.Zero) Marshal.FreeHGlobal(formatPointer); }
    }

    public void StartRecording()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (worker is not null) throw new InvalidOperationException("Capture has already started.");
        Marshal.ThrowExceptionForHR(buffer.Start(1)); // DSCBSTART_LOOPING
        worker = new Thread(CaptureLoop) { IsBackground = true, Name = "SharpAmp DirectSound capture" };
        worker.Start();
    }

    private void CaptureLoop()
    {
        Exception? error = null;
        var position = 0;
        var lastPoll = Stopwatch.GetTimestamp();
        var duration = TimeSpan.FromSeconds((double)bytes.Length / format.AverageBytesPerSecond);
        try
        {
            while (!stop.WaitOne(pollMilliseconds))
            {
                // After a full lap cursor arithmetic is ambiguous. Report data loss instead of replaying stale samples.
                if (Stopwatch.GetElapsedTime(lastPoll) >= duration)
                    throw new InvalidOperationException("DirectSound capture overran its native buffer.");
                lastPoll = Stopwatch.GetTimestamp();
                Marshal.ThrowExceptionForHR(buffer.GetCurrentPosition(out _, out var readPosition));
                readPosition -= readPosition % format.BlockAlign;
                var count = (readPosition - position + bytes.Length) % bytes.Length;
                if (count == 0) continue;
                Marshal.ThrowExceptionForHR(buffer.Lock(position, count, out var first, out var firstLength, out var second, out var secondLength, 0));
                try
                {
                    Marshal.Copy(first, bytes, 0, firstLength);
                    if (secondLength != 0) Marshal.Copy(second, bytes, firstLength, secondLength);
                }
                finally { Marshal.ThrowExceptionForHR(buffer.Unlock(first, firstLength, second, secondLength)); }
                position = readPosition;
                DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, count));
            }
        }
        catch (Exception exception) { error = exception; }
        finally
        {
            try { Marshal.ThrowExceptionForHR(buffer.Stop()); }
            catch (Exception exception) { error ??= exception; }
            RecordingStopped?.Invoke(this, new StoppedEventArgs(error));
        }
    }

    public void StopRecording()
    {
        if (disposed) return;
        stop.Set();
        if (worker is not null && worker != Thread.CurrentThread) worker.Join();
    }

    public void Dispose()
    {
        if (disposed) return;
        StopRecording();
        disposed = true;
        Marshal.ReleaseComObject(buffer);
        Marshal.ReleaseComObject(capture);
        stop.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CaptureBufferDescription
    {
        public int Size, Flags, BufferBytes, Reserved;
        public IntPtr Format;
        public int EffectCount;
        public IntPtr Effects;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumerateCallback(IntPtr guid, IntPtr description, IntPtr module, IntPtr context);

    [DllImport("dsound.dll", EntryPoint = "DirectSoundCaptureEnumerateW", ExactSpelling = true)]
    private static extern int DirectSoundCaptureEnumerate(EnumerateCallback callback, IntPtr context);

    [DllImport("dsound.dll", ExactSpelling = true)]
    private static extern int DirectSoundCaptureCreate(ref Guid device, out NativeCapture capture, IntPtr outer);

    // Vtable order follows dsound.h, including unused methods before the methods we invoke.
    [ComImport, Guid("B0210781-89CD-11D0-AF08-00A0C925CD16"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface NativeCapture
    {
        [PreserveSig] int CreateCaptureBuffer(ref CaptureBufferDescription description, out NativeBuffer buffer, IntPtr outer);
        [PreserveSig] int GetCaps(IntPtr caps);
        [PreserveSig] int Initialize(ref Guid device);
    }

    [ComImport, Guid("B0210782-89CD-11D0-AF08-00A0C925CD16"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface NativeBuffer
    {
        [PreserveSig] int GetCaps(IntPtr caps);
        [PreserveSig] int GetCurrentPosition(out int capturePosition, out int readPosition);
        [PreserveSig] int GetFormat(IntPtr format, int size, out int written);
        [PreserveSig] int GetStatus(out int status);
        [PreserveSig] int Initialize(IntPtr capture, IntPtr description);
        [PreserveSig] int Lock(int offset, int count, out IntPtr first, out int firstLength, out IntPtr second, out int secondLength, int flags);
        [PreserveSig] int Start(int flags);
        [PreserveSig] int Stop();
        [PreserveSig] int Unlock(IntPtr first, int firstLength, IntPtr second, int secondLength);
    }
}
