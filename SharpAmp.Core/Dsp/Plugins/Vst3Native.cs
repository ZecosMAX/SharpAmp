using System.Runtime.InteropServices;

namespace SharpAmp.Core.Dsp.Plugins;

// C ABI of Vst3HostSharp 1.0.568-r's packaged Vst3Pont bridge. Keeping the process
// data pointer here lets us set the actual frame count, including short final blocks.
// The bridge owns process data and channel-pointer arrays; the host pins audio buffers.
// Each instance loads a private copy because the bridge's global plug-in registry
// has no documented synchronization. Loading/removing one effect must not race
// another effect's audio callback, nor make that callback wait for a global lock.
internal sealed class Vst3Native : IDisposable
{
    private IntPtr library;
    private string? directory;
    private string? libraryPath;

    internal readonly LoadPluginDelegate LoadVst3Ext;
    internal readonly PluginDelegate DisposePlugin;
    internal readonly SetupDelegate SetupAudioProcessor;
    internal readonly ProcessingDelegate SetProcessing;
    internal readonly PluginDelegate Process;
    internal readonly BusCountDelegate GetBusCount;
    internal readonly BusInfoDelegate GetBusInfo;
    internal readonly ActivateDelegate ActivateBus;
    internal readonly PluginDelegate GetParameterCount;
    internal readonly ParameterInfoDelegate GetParameterInfo;
    internal readonly GetParameterDelegate GetParameterNormalized;
    internal readonly SetParameterDelegate SetParameterNormalized;
    internal readonly SetParameterDelegate AddParameterData;

    internal Vst3Native()
    {
        try
        {
            // Resolve the NuGet runtime asset through .NET so both framework-dependent
            // builds (runtimes/win-x64/native) and RID-specific publishes work.
            var packaged = NativeLibrary.Load("Vst3Pont", typeof(Vst3HostSharp.Vst3Plugin).Assembly, null);
            string packagedPath;
            try
            {
                var path = new char[32768];
                var length = GetModuleFileName(packaged, path, path.Length);
                if (length == 0 || length >= path.Length)
                    throw new InvalidOperationException("Could not resolve the packaged VST3 bridge.");
                packagedPath = new string(path, 0, (int)length);
            }
            finally { NativeLibrary.Free(packaged); }

            directory = Path.Combine(Path.GetTempPath(), "SharpAmp-vst3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            libraryPath = Path.Combine(directory, "Vst3Pont.dll");
            File.Copy(packagedPath, libraryPath);
            library = NativeLibrary.Load(libraryPath, typeof(Vst3Native).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories);

            LoadVst3Ext = Export<LoadPluginDelegate>(nameof(LoadVst3Ext));
            DisposePlugin = Export<PluginDelegate>(nameof(DisposePlugin));
            SetupAudioProcessor = Export<SetupDelegate>(nameof(SetupAudioProcessor));
            SetProcessing = Export<ProcessingDelegate>(nameof(SetProcessing));
            Process = Export<PluginDelegate>(nameof(Process));
            GetBusCount = Export<BusCountDelegate>(nameof(GetBusCount));
            GetBusInfo = Export<BusInfoDelegate>(nameof(GetBusInfo));
            ActivateBus = Export<ActivateDelegate>(nameof(ActivateBus));
            GetParameterCount = Export<PluginDelegate>(nameof(GetParameterCount));
            GetParameterInfo = Export<ParameterInfoDelegate>(nameof(GetParameterInfo));
            GetParameterNormalized = Export<GetParameterDelegate>(nameof(GetParameterNormalized));
            SetParameterNormalized = Export<SetParameterDelegate>(nameof(SetParameterNormalized));
            AddParameterData = Export<SetParameterDelegate>(nameof(AddParameterData));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    public void Dispose()
    {
        if (library != IntPtr.Zero)
        {
            NativeLibrary.Free(library);
            library = IntPtr.Zero;
        }
        // Exact task-owned paths only. Antivirus/file indexing can briefly keep the
        // file open; such a cleanup error must not bring down the audio session.
        try
        {
            if (libraryPath is not null) File.Delete(libraryPath);
            if (directory is not null) Directory.Delete(directory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        libraryPath = null;
        directory = null;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileName(IntPtr module, [Out] char[] path, int size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    internal delegate int LoadPluginDelegate(string path, ref int hasEditor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int PluginDelegate(int pluginId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int SetupDelegate(int pluginId, int bufferSize, ref IntPtr processData, int parameterCount, double sampleRate);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ProcessingDelegate(int pluginId, int state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int BusCountDelegate(int pluginId, int mediaType, int direction);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int BusInfoDelegate(int pluginId, int mediaType, int direction, int index, out BusInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ActivateDelegate(int pluginId, int mediaType, int direction, int index, byte state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ParameterInfoDelegate(int pluginId, int index, out ParameterInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate double GetParameterDelegate(int pluginId, uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int SetParameterDelegate(int pluginId, uint id, double value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct BusInfo
    {
        internal int MediaType;
        internal int Direction;
        internal int ChannelCount;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Name;
        internal int BusType;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ParameterInfo
    {
        internal uint Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Title;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string ShortTitle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Units;
        internal int StepCount;
        internal double DefaultNormalizedValue;
        internal int UnitId;
        internal int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessData
    {
        internal int ProcessMode;
        internal int SymbolicSampleSize;
        internal int NumSamples;
        internal int NumInputs;
        internal int NumOutputs;
        internal IntPtr Inputs;
        internal IntPtr Outputs;
        internal IntPtr InputParameterChanges;
        internal IntPtr OutputParameterChanges;
        internal IntPtr InputEvents;
        internal IntPtr OutputEvents;
        internal IntPtr ProcessContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioBusBuffers
    {
        internal int NumChannels;
        internal ulong SilenceFlags;
        internal IntPtr Channels;
    }
}
