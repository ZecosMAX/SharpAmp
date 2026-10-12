# Introduction

Have you ever tried to find a very simple (and i.e. fast) guitar amp simulator, so that you could plug your guitar into your PC, and play alongside songsterr; or similar projects?

And have you found out that even simplest of those require you to ***purchase*** that software, even for $0.0, making you create an account, share your personal information, only to find out that software requires internet connection to work (??)

<sub>*looking at you two: GR7, amplitron*</sub>

And have you found out that more complex solutions, while delivering a good techincal basis and good UI, for some unknown reason just refuse to work with ASIO and output anywhere else?

Well, I for sure did. That's why I decided to build a piece of software that is: free (forever), open-source and simple as a rock.

# SharpAmp

A small Windows/.NET 10 audio router built on NAudio 2.2.1, with an extensible float DSP chain.

```text
Selected input → bounded capture queue → float conversion → sample-rate conversion
              → channel mapping → IDigitalSignalProcessor chain → selected output
```

The solution contains four projects:

| Project | Purpose |
| --- | --- |
| `SharpAmp.Core` | Device enumeration, audio sessions, DSP interface, gain, drive, delay, modulation, lo-fi effects, reverb, and demo chain |
| `SharpAmp.Cli` | Device selection and live processing from the terminal |
| `SharpAmp.Avalonia` | Desktop processor chain, parameter sliders, audio settings, and engine start/stop |
| `SharpAmp.Tests` | Hardware-independent xUnit tests for DSP, sample conversion, and session lifecycle |

## Installation

### Linux
Currently, Core lib and cli target `.net10.0-windows` because all of the audio shenanigans. So there is no support for linux, yet :(
I plan to add it, when I'll be able to test on unix machine. Or you can contribute!

### Windows
Download and install .NET 10.0.12 Runtime directly from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

or;

Open a terminal, and install via `winget` in the directory of your choosing
```Bash
winget install -e --id Microsoft.DotNet.Runtime.10
```

Check the versions of installed dotnet runtimes

```Bash
dotnet --list-runtimes
```

It should output something like this:
```Bash
Microsoft.AspNetCore.App 10.0.12 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
Microsoft.NETCore.App 10.0.12 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
Microsoft.WindowsDesktop.App 10.0.12 [C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App]
```
If it does, you're good to go, if it doesn't try restarting your terminal or PC altogether

Next, clone the repository
```
git clone https://github.com/ZecosMAX/SharpAmp.git
```

Enter repository directory
```Bash
cd SharpAmp
```

Proceed to the Run section

## Run

### Desktop GUI

```powershell
dotnet run --project SharpAmp.Avalonia
```

The main view starts with **hard overdrive → reverb**, displayed as processor cards connected by arrows. Adjust the parameter sliders, select input and output devices in **Settings**, then use **Start audio** in the top-right corner. Stop audio before changing parameters or device settings; each start creates a fresh session with the current values. Device errors appear below the header, and closing the window releases the audio session.

### Command line

Install the .NET 10 SDK on Windows, then run from the solution directory:

```powershell
dotnet restore SharpAmp.slnx
dotnet build SharpAmp.slnx
dotnet test SharpAmp.slnx
dotnet run --project SharpAmp.Cli -- list
dotnet run --project SharpAmp.Cli -- run --chain "presets/guitar_(clean).json"
```

`run --chain PATH` loads a JSON preset and prompts for an input and output index. The two lists have separate indices. For a noninteractive session, select indices from the current list:

```powershell
dotnet run --project SharpAmp.Cli -- run --input 0 --output 1 --chain "presets/guitar_(distorted).json"
dotnet run --project SharpAmp.Cli -- run --input 0 --output 1 --bypass --seconds 10
dotnet run --project SharpAmp.Cli -- --help
```

Press **Ctrl+C** to stop. Use headphones and start at a low output volume: routing a microphone to nearby speakers can cause acoustic feedback. No audio is saved to disk.

Device indices can change after plugging devices in or restarting Windows. Library callers can retain an `AudioDevice` backend/ID pair instead; MME IDs are themselves device indices and should be enumerated again.

## Use the library

```csharp
using SharpAmp.Core.Audio;
using SharpAmp.Core.Dsp;

var inputs = AudioDeviceCatalog.GetDevices(AudioBackend.Wasapi, AudioDirection.Input);
var outputs = AudioDeviceCatalog.GetDevices(AudioBackend.Wasapi, AudioDirection.Output);
if (inputs.Count == 0 || outputs.Count == 0)
    throw new InvalidOperationException("Select an available input and output device.");

var options = new AudioSessionOptions(inputs[0], outputs[0])
{
    SampleRate = 48000,
    OutputChannels = 2,
    LatencyMilliseconds = 50,
    BufferMilliseconds = 250
};

using var session = new AudioSession(options, DemoProcessorChain.Create());
session.Start();
Console.ReadLine();
session.Stop();
if (await session.Completion is { } error)
    Console.Error.WriteLine(error.Message);
```

`AudioSession` owns the native capture/playback resources. It is single use: dispose it and create another session to change devices, format, or processors. `Stop()` is idempotent and cuts the stream immediately, including the reverb tail. `Completion` returns `null` on a requested stop or an exception on failure; applications can await it to detect a disconnected device or failed processor. `DroppedInputFrames` reports capture queue overflow. Explicit disposal is required even after completion.

Input and output can use different backends, for example ASIO input to WASAPI output. No default device is silently substituted when a selected device fails.

## Device support

| Backend | Input | Output | Format / selection |
| --- | --- | --- | --- |
| MME | NAudio `WaveInEvent` | NAudio `WaveOutEvent` | Device index; requested rate and channel count, PCM16 |
| WASAPI | NAudio `WasapiCapture` | NAudio `WasapiOut` | Endpoint ID; shared mode; capture uses native mix format |
| ASIO | NAudio `AsioOut` recording | NAudio `AsioOut` playback | Driver name; requested rate, channel count, and zero-based channel offsets |
| DX / DirectSound | Native DirectSound capture adapter implementing NAudio `IWaveIn` | NAudio `DirectSoundOut` | Device GUID; requested rate and channel count, PCM16 |

NAudio 2.2.1 has no DirectSound capture class, so the small adapter in `DirectSoundCapture.cs` supplies capture enumeration and a polling ring buffer through `dsound.dll`. It reports native buffer overruns as session failures. The interop layout follows Microsoft's [DSCBUFFERDESC documentation](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee416823(v=vs.85)).

When both ends select the same ASIO driver, one `AsioOut` instance handles full duplex. Its capture callback fills the queue before NAudio reads playback samples in that same buffer cycle. This avoids trying to open one exclusive ASIO driver twice. Different ASIO drivers are opened independently; their ability to coexist depends on the drivers. ASIO listing reports installed driver names without loading them, so unavailable hardware or unsupported input/output directions are detected when opening the session. ASIO sample formats are those supported by NAudio 2.2.1, including Int16/24/32 LSB and Float32 LSB capture.

`InputChannels` applies to MME, DirectSound, and ASIO. WASAPI captures all channels in the endpoint's mix format. `OutputChannels` sets the DSP/output channel count. Mono is duplicated to every output channel; multiple channels downmix to mono by averaging. Otherwise channels map by index, extra input channels are dropped, and extra output channels are silent. ASIO offsets select a contiguous range of hardware channels; offsets on other backends are rejected.

Capture is converted to interleaved 32-bit float and resampled to `SampleRate` before processing. WASAPI shared playback can additionally convert to the endpoint's mix format. MME, DirectSound, and ASIO must support the requested playback format. Final samples are clipped to `[-1, 1]`, with nonfinite values replaced by silence, and legacy outputs are converted to PCM16.

`LatencyMilliseconds` is a request, not an end-to-end guarantee. ASIO uses its driver's configured buffer size. The capture queue capacity is separate from output latency: it does not deliberately accumulate a full buffer before playback. Underflow emits silence; overflow drops whole incoming packets and increments `DroppedInputFrames`. Independent devices can have different clock rates even at the same nominal sample rate. This simple wrapper uses fixed-rate resampling, not adaptive clock synchronization, so long-running routes between unrelated devices can experience occasional drops or silence. Use a single duplex ASIO device when low latency and a shared clock matter.

## Save and load processor chains

Save a preset as JSON and select it when starting the CLI:

```powershell
dotnet run --project SharpAmp.Cli -- run --chain presets/overdrive-room.json
dotnet run --project SharpAmp.Cli -- run --input 0 --output 1 --chain "C:\My presets\clean.json"
```

`run` requires `--chain PATH` to a local JSON preset, or `--bypass` to skip all effects. Bypass overrides the preset regardless of argument order, without opening or validating its file; audio routing and format options still apply. Effect settings are configured only in JSON; there is no demo-chain fallback. Files are read once at session creation unless bypassed; select another file on the next run to change chains. See [Docs](Docs/README.md) for every effect and the [preset guide](Docs/Presets.md) for serialization, parameters, and complete examples.

At startup, the CLI prints the active effects in processing order, including all effective settings (even defaults omitted from the preset):

```text
Processor chain (in processing order):
  1. Hard overdrive — drive: 7, output gain: 0.35
  2. Reverb — wet mix: 0.2, feedback: 0.7, damping: 0.3
```

An empty JSON chain or `--bypass` prints `Processor chain: bypass (no effects)`.

```json
{
  "processors": [
    { "type": "hardOverdrive", "parameters": { "drive": 7, "outputGain": 0.35 } },
    { "type": "reverb", "parameters": { "wet": 0.2, "feedback": 0.7, "damping": 0.3 } }
  ]
}
```

Processors run in array order. Each effect page in the [effect reference](Docs/README.md#effect-reference) documents its JSON type, defaults, ranges, and behavior. To create your own preset, copy [custom-chain.json](Docs/Examples/custom-chain.json) and follow the [preset format and C# serialization guide](Docs/Presets.md).

### Basic guitar and bass presets

The `presets` directory includes six starting points. Quote paths containing parentheses in PowerShell:

```powershell
dotnet run --project SharpAmp.Cli -- run --chain "presets/guitar_(clean).json"
dotnet run --project SharpAmp.Cli -- run --chain "presets/bass_(distorted).json"
```

| Preset | Chain and intended sound |
| --- | --- |
| `guitar_(clean).json` | Unity gain → light room reverb; clean rhythm or arpeggios. |
| `guitar_(distorted).json` | Gain → soft distortion → subtle tape echo → room reverb; moderate rock crunch. |
| `guitar_(heavy).json` | Gain → hard overdrive → very light room reverb; strong clipping with little ambience. |
| `bass_(clean).json` | Unity gain; uncolored direct bass. |
| `bass_(distorted).json` | Gain → soft distortion, 45% wet; grit blended with the clean signal. |
| `bass_(heavy).json` | Gain → stronger soft distortion, 70% wet; aggressive drive with some clean definition. |

These are original, conservative starting settings, not amp models or measured artist tones. Adjust the first `gain` for pickup/interface level; adjust distortion `outputGain` for listening level. Clean presets start at unity to avoid clipping a hot input. Bass presets omit delay/reverb and retain a dry blend in the driven sounds, an approach also used by the [BOSS ODB-3](https://www.boss.info/us/products/odb-3/). Conventional bass rigs often include compression and EQ, as illustrated by [Fender's Downtown Express](https://www.fender.com/products/downtown-express-bass-multi-effect-pedal); those processors are not implemented here. No cabinet simulation is included, so direct distorted tones can remain bright.

## Add a DSP class

Implement `IDigitalSignalProcessor` and place it in a `ProcessorChain`:

Effect implementations live in `SharpAmp.Core/Dsp/Processors`, under the `SharpAmp.Core.Dsp.Processors` namespace. The interface, chain, and demo factory remain in `SharpAmp.Core.Dsp`. Code referencing `DistortionProcessor` or `ReverbProcessor` should import the processors namespace.

```csharp
using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Processors;

public sealed class PolarityProcessor : IDigitalSignalProcessor
{
    public void Initialize(int sampleRate, int channels) { }
    public void Process(Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
            samples[i] = -samples[i];
    }
    public void Reset() { }
}

var chain = new ProcessorChain(
    new PolarityProcessor(),
    new GainProcessor(0.5f),
    new DistortionProcessor(drive: 3, outputGain: 0.35f),
    new ReverbProcessor(wet: 0.2f, feedback: 0.7f, damping: 0.3f));
```

Processors run in declaration order on the playback thread, after format conversion. `Initialize` receives the processing sample rate and channel count and must clear prior state. Blocks contain complete interleaved frames; their size can vary. Process synchronously, avoid blocking/I/O or per-sample allocations, and do not retain the sample span. Each session needs its own processor instances. Chain membership and effect settings are fixed for the session; do not call `Reset` or `Initialize` concurrently with processing. An empty chain provides bypass.

The library demo is **hard overdrive → reverb**. The CLI loads the requested JSON preset unless `--bypass` skips effects. The separate `DistortionProcessor` uses a tanh waveshaper and output gain. Reverb uses four parallel damped feedback combs per channel, with independent channel history and normalized wet gain. It is a basic algorithmic reverb, not convolution or a room simulation.

For hard clipping, use `HardOverdriveProcessor`: it multiplies each sample by `Drive`, clamps to `[-1, 1]`, then applies `OutputGain`. The default drive is 5 and output gain is 0.35. Drive must be finite and positive; output gain must be finite and in `[0, 1]`. It is stateless and processes each channel independently.

```csharp
var overdriveChain = new ProcessorChain(
    new HardOverdriveProcessor(drive: 5, outputGain: 0.35f),
    new ReverbProcessor(wet: 0.2f));
```

## Verification

The xUnit suite uses simulated audio devices and checks DSP order, clipping, reverb decay/reset/channel isolation, PCM conversion, resampling, channel mapping, queue boundaries, lifecycle, and fault cleanup. It never opens physical audio devices. CLI help and enumeration can be checked without starting a stream.

For manual hardware verification, list devices, select each intended input/output combination, and run a short session with `--seconds 10` at low output volume. Check mono/stereo routing, ASIO channel offsets, Ctrl+C shutdown, and device disconnect handling. Hardware compatibility and audible latency require testing with the actual drivers; automated tests do not establish them.

## Current milestones

- [ ] Create a UI project in Avalonia with configurable processors.
- [ ] Add VST plugins support.
- [ ] Add more effect processors.
- [ ] Idk? learn a guitar finally.

## Contributing

Bug reports, feature suggestions, documentation improvements, and code contributions are welcome. See [Contributing](CONTRIBUTING.md) for setup and pull request guidelines, and follow our [Code of Conduct](CODE_OF_CONDUCT.md) when participating.

## Disclosure

I **do** use AI in my projects. Don't make a fuss about it, every single developer in .Net business space that wants to have a job does. However, I **do not** vibe-code, aka promt 'create me a software that generates me a million bucks'. And I highly condemn those who refuse to learn from their projects

IMO, problem decomposition is still, and will be, essential skill in programming, therefore, any PR that is an obviuous AI slop will be closed.
