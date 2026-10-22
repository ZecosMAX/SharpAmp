# SharpAmp documentation

Start with the [desktop pedalboard guide](Pedalboard.md) to build and edit a sound visually, use keyboard controls, and save presets and layouts.

For the CLI and library, see [Creating and serializing presets](Presets.md) for the JSON format, loading instructions, effect order, and a complete example. Copy [Examples/custom-chain.json](Examples/custom-chain.json) to start your own chain, or [Examples/bypass.json](Examples/bypass.json) for an unprocessed signal. The desktop imports this format and saves its own richer preset format.

## Effect reference

Every page includes the exact serialized type, all parameters with defaults and ranges, a complete JSON preset, and processing notes.

| Effect | JSON type | Purpose |
| --- | --- | --- |
| [Gain](Effects/gain.md) | `gain` | Linear boost or attenuation. |
| [Hard overdrive](Effects/hardOverdrive.md) | `hardOverdrive` | Hard clipping with output level control. |
| [Distortion](Effects/distortion.md) | `distortion` | Soft clipping with a clean blend. |
| [Reverb](Effects/reverb.md) | `reverb` | Small algorithmic room-style ambience. |
| [Delay](Effects/delay.md) | `delay` | Clean repeating echoes. |
| [Tape delay](Effects/tapeDelay.md) | `tapeDelay` | Saturated, filtered echoes with pitch wobble. |
| [Tremolo](Effects/tremolo.md) | `tremolo` | Rhythmic volume modulation. |
| [Bitcrusher](Effects/bitcrusher.md) | `bitcrusher` | Reduced bit depth and sample-and-hold rate reduction. |
| [Octave fuzz](Effects/octaveFuzz.md) | `octaveFuzz` | Rectified fuzz with an octave-up character. |
| [Ring modulator](Effects/ringModulator.md) | `ringModulator` | Metallic modulation by a sine carrier. |

## CLI quick start

Run these commands from the repository root:

```powershell
dotnet run --project SharpAmp.Cli -- list
dotnet run --project SharpAmp.Cli -- run --chain "presets/guitar_(clean).json"
dotnet run --project SharpAmp.Cli -- run --input 0 --output 1 --chain Docs/Examples/custom-chain.json
dotnet run --project SharpAmp.Cli -- run --bypass
dotnet run --project SharpAmp.Cli -- --help
```

`run` requires `--chain PATH` unless `--bypass` is supplied. Bypass skips every effect and overrides any preset without reading its file. Input/output selection remains interactive unless indices are supplied. Paths are relative to the current working directory; absolute paths work too. Quote paths containing spaces or parentheses. `--help` lists audio options and the effect parameter reference; effect parameters belong inside JSON, not on the command line.

For the six basic guitar and bass presets, device support, and library usage, see the [project README](../README.md).
