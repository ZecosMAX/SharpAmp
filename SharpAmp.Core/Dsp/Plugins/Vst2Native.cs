using System.Runtime.InteropServices;

namespace SharpAmp.Core.Dsp.Plugins;

// VST 2 uses a C ABI with pointer-sized dispatcher values and 32-bit audio/parameter values.
internal static class Vst2Native
{
    internal const int Magic = 0x56737450;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint Callback(nint effect, int opcode, int index, nint value, nint pointer, float option);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint EntryPoint(Callback host);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void Process(nint effect, nint inputs, nint outputs, int frameCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SetParameter(nint effect, int index, float value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate float GetParameter(nint effect, int index);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Effect
    {
        public int Magic;
        public nint Dispatcher;
        public nint Process;
        public nint SetParameter;
        public nint GetParameter;
        public int ProgramCount;
        public int ParameterCount;
        public int InputCount;
        public int OutputCount;
        public int Flags;
        public nint Reserved1;
        public nint Reserved2;
        public int InitialDelay;
        public int RealQualities;
        public int OfflineQualities;
        public float IoRatio;
        public nint Object;
        public nint User;
        public int UniqueId;
        public int Version;
        public nint ProcessReplacing;
        // Only the prefix through processReplacing is needed, including for pre-2.4 plug-ins.
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TimeInfo
    {
        public double SamplePosition;
        public double SampleRate;
        public double Nanoseconds;
        public double QuarterNotePosition;
        public double Tempo;
        public double BarStartPosition;
        public double CycleStartPosition;
        public double CycleEndPosition;
        public int TimeSignatureNumerator;
        public int TimeSignatureDenominator;
        public int SmpteOffset;
        public int SmpteFrameRate;
        public int SamplesToNextClock;
        public int Flags;
    }
}
