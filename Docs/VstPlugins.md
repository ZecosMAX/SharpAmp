# VST effect plug-ins

SharpAmp hosts VST2 `.dll` and VST3 `.vst3` audio effects alongside its built-in processors. VST3 hosting requires Windows x64; VST2 binaries must match the process architecture. There is no bridge for 32-bit plug-ins in a 64-bit process.

## Pedalboard

Click **+ VST…**, then choose a plug-in file or a VST3 bundle folder. A bundle is the directory ending in `.vst3`, containing `Contents/x86_64-win`. Adding probes the plug-in at 48 kHz stereo to obtain its parameters, then releases that instance. Connect the new block into the INPUT → OUTPUT path as with any other effect. Playback opens a fresh instance at the session's actual sample rate.

The inspector exposes writable parameters as normalized sliders from 0 to 1. You can add, duplicate, disconnect, bypass, reorder, and remove VST effects during playback. Changes rebuild the chain and crossfade over 10 ms, resetting effect history. A failed replacement leaves the previous sound playing and reports an error.

Preset files store the absolute plug-in path and normalized parameter values. They do not include the plug-in binary. Loading a preset does not load native code until you start audio or publish a connected chain into an active session. Reopened presets show parameter IDs; newly added plug-ins show their parameter names. Install the same plug-in at the saved path on another computer, or edit the path in the preset.

## Runtime API

`RuntimeProcessorChain` implements `IDigitalSignalProcessor` and can be passed directly to an `AudioSession`. Add effects before starting, or add/remove/replace effects from a control thread while audio runs:

```csharp
using SharpAmp.Core.Audio;
using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Configuration;

// options is your configured AudioSessionOptions.
using var effects = new RuntimeProcessorChain();
effects.Add(new ProcessorConfiguration
{
    Type = "gain", Parameters = new() { ["gain"] = 0.5f }
});

using var session = new AudioSession(options, effects);
session.Start();

// Call on the control thread, never inside an audio callback.
var index = effects.AddVst(@"C:\Program Files\Common Files\VST3\MyEffect.vst3");
// Or: effects.AddVst(@"C:\VstPlugins\MyEffect.dll");
// Optional second argument: IReadOnlyDictionary<uint, float> parameter values.

effects.RemoveAt(index);
effects.IsBypassed = true;
session.Stop();
// using declarations dispose the session first, then the owned effects.
```

Indices refer to the current order; removal shifts subsequent indices. `ReplaceAt(index, configuration)` changes an effect's settings or type. `Clear()` publishes an empty bypass chain. `Configurations` returns independent snapshots. Runtime edits are transactional after initialization: a load or parameter error leaves both configuration and playing chain unchanged. Before initialization, native loading is deferred to session creation.

For custom chains, construct `VstPluginProcessor(path, VstPluginFormat.Vst2 / Vst3, parameters)` and include it in a `ProcessorChain`. After `Initialize`, its `Parameters` property lists writable parameter IDs, names, and initial normalized values. For VST2, IDs are zero-based indices; VST3 uses plug-in-defined IDs, which need not be contiguous. Publish fresh chains with `LiveProcessorChain.Replace` to change them during playback.

Chains own their disposable processors. Dispose `AudioSession` before disposing its chain; the session owns its audio devices, while callers own the supplied processor. Do not share processor instances between chains. Retired live chains are disposed off the audio callback after their crossfade finishes. Native creation, configuration, and cleanup stay on a dedicated plug-in control thread. `Initialize`, `Reset`, and `Dispose` require a stopped stream.

## CLI and preset configuration

Use `type: "vst2"` or `type: "vst3"`, an absolute `pluginPath`, and decimal parameter IDs as keys in `parameters`. Values must be finite and in `[0, 1]`. An empty parameters object uses the plug-in's initial settings. For example, save this as a CLI/Core preset and pass it to `run --chain` (replace the path and ID with those from your installed effect):

```jsonc
{
  "processors": [
    {
      "type": "vst3",
      "pluginPath": "C:\\Program Files\\Common Files\\VST3\\MyEffect.vst3",
      "parameters": { "7": 0.5 }
    }
  ]
}
```

The actual preset is ordinary JSON; comments are not accepted. VST2 paths must end in `.dll`; VST3 paths must end in `.vst3`. Numeric IDs must use canonical decimal notation (for example `7`, not `07`). Unknown IDs are rejected when the plug-in initializes.

## Supported scope

- Mono and stereo audio effects with one main input and output. Mono input is duplicated for a stereo plug-in; stereo is averaged for a mono plug-in. Outputs are similarly adapted to the session's channel count.
- VST3 auxiliary buses are inactive and receive silence. Instruments, MIDI, sidechain routing, surround, transport automation, and native plug-in editor windows are not provided.
- Only normalized parameters are saved. Opaque state chunks, factory preset selection, external sample/impulse files, and other state not represented by parameters are not restored. Plug-in latency is not compensated during crossfades.
- Plug-ins run in the SharpAmp process. Native crashes can terminate the application; load trusted, compatible effects. A plug-in that requires a native editor for activation or configuration may need setup in another host first.
- The VST3 bridge comes from the pinned MIT-licensed [Vst3HostSharp](https://github.com/cuikp/Vst3HostSharp) package. Its native runtime must be deployed with the app; normal SDK build/publish copies it from NuGet.

## Verification

Automated tests cover parameter validation, preset round trips, variable buffer sizes, channel conversion, failure cleanup, runtime edits, and safe retirement. The VST3 integration test can be run against an installed effect using `SHARPAMP_TEST_VST3_PATH`; see its test source for the command. Real plug-in compatibility and audible behavior still require testing with your effects and devices.
