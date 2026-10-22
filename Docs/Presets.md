# Creating and serializing presets

[Documentation index](README.md)

This guide describes the **CLI/Core preset format**. A preset contains effect types and settings; device choices, sample rate, channels, and run duration remain CLI options. The [desktop pedalboard](Pedalboard.md) imports these files but saves a richer `.sharpamp.json` format containing names, effect IDs, explicit connections, and individual bypass states. Desktop saves are not accepted directly by the CLI/Core loader.

## Create your first chain

Copy [Examples/custom-chain.json](Examples/custom-chain.json), edit its values, and load the file:

```powershell
dotnet run --project SharpAmp.Cli -- run --chain Docs/Examples/custom-chain.json
```

The example applies gain, soft distortion, delay, and a small amount of reverb:

```json
{
  "processors": [
    { "type": "gain", "parameters": { "gain": 1.25 } },
    { "type": "distortion", "parameters": { "drive": 3, "outputGain": 0.4, "wet": 0.8 } },
    { "type": "delay", "parameters": { "timeMs": 250, "feedback": 0.25, "wet": 0.15 } },
    { "type": "reverb", "parameters": { "wet": 0.1, "feedback": 0.5, "damping": 0.4 } }
  ]
}
```

Add an effect by inserting another object into the array, separated by a comma. Remove one by deleting its object. Reorder objects to change processing order. You can use the same type more than once, for example gain at the beginning and end. Each instance has its own settings and history.

The file is read when the session is created. Editing it during playback does not change that session; stop and restart to load the new settings.

## JSON schema

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `processors` | Array of objects | Yes | Effects in processing order. An empty array is bypass. |
| `processors[i].type` | String | Yes | Exact effect identifier from the [effect reference](README.md#effect-reference), such as `tapeDelay`. |
| `processors[i].parameters` | Object of numeric values | No | Settings for this effect. Omitted settings use constructor defaults. |

Only these fields are supported. An effect object does not accept a name, enabled flag, or nested chain. To disable an effect, remove it from the array. An omitted `parameters` object and an empty one have the same meaning:

```json
{
  "processors": [
    { "type": "gain" },
    { "type": "delay", "parameters": {} }
  ]
}
```

Both effects above use their documented defaults. Each [effect page](README.md#effect-reference) provides a complete preset with all parameter keys written out.

### Values and validation

- Names are case-sensitive: `tapeDelay` is valid; `TapeDelay` and `tape-delay` are not.
- Parameters are JSON numbers, not quoted strings, booleans, or unit-suffixed text. Use `250` for milliseconds and `0.25` for a mix; decimals use a period regardless of system locale.
- All values must be finite and fit the supported single-precision parameter representation. `NaN`, infinity, and numeric overflow are rejected.
- `bits` and `downsample` must be whole numbers in their documented ranges. Other parameters can be fractional.
- `0..1` includes both endpoints. `0 <= value < 1` excludes 1. `drive` must be strictly positive; `gain` may be zero.
- Unknown types, keys, duplicate properties, missing required fields, null values, comments, and trailing commas are rejected. Do not add metadata fields to a preset.

The CLI reports errors instead of substituting another chain. Invalid effect settings include the zero-based processor index and parameter where available, for example `processors[1].parameters.drive`. A missing file is an error; there is no demo-chain fallback.

## Bypass

Use `--bypass` to skip all effects, with or without a preset path:

```powershell
dotnet run --project SharpAmp.Cli -- run --bypass
dotnet run --project SharpAmp.Cli -- run --chain "presets/guitar_(heavy).json" --bypass
```

`--bypass` takes precedence regardless of argument order. The preset file is not opened or validated, so a missing, unreadable, or invalid preset does not prevent bypass. Audio device, format, and duration options still apply and are validated normally, as is command-line syntax. Format conversion and final output clipping still run; only the effect chain is empty.

An empty processor array routes audio through the usual format conversion and output handling with no effects:

```json
{
  "processors": []
}
```

Use the supplied [bypass file](Examples/bypass.json):

```powershell
dotnet run --project SharpAmp.Cli -- run --chain Docs/Examples/bypass.json
```

## Levels, order, and units

Gain and outputGain are linear amplitude multipliers, not decibels. A multiplier of 2 is approximately +6 dB. Put gain before distortion to change saturation; put it after an effect to adjust that effect's output level. Adjust output level when comparing drive settings.

A conventional starting order is gain → drive → modulation → delay → reverb, but array order is entirely yours. Delay before distortion also distorts the repeats; delay after distortion keeps them cleaner. Effect pages explain where mix/output controls apply and where feedback normalization changes echo level.

`wet` is a linear mixture proportion, `timeMs` and `wowDepthMs` are milliseconds, and `rateHz`, `frequencyHz`, `toneHz`, and `wowRateHz` use Hz. Feedback and damping are dimensionless coefficients. They are not seconds or percentages written as 0..100.

Intermediate signals can exceed ±1; final playback clips to that range and replaces nonfinite samples with silence. Presets contain no cabinet simulation. These effects use simple DSP algorithms; the effect pages describe their limitations.

## Serialization from C#

The configuration model is separate from a running processor chain:

```csharp
using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Configuration;

var json = File.ReadAllText("Docs/Examples/custom-chain.json");
var preset = ProcessorChainConfiguration.FromJson(json);
preset.Processors[0].Parameters["gain"] = 1.5f;

// Build to validate effect names and parameter ranges before saving.
var chain = ProcessorChain.FromConfiguration(preset);
File.WriteAllText("my-preset.json", preset.ToJson());

// Or load and build in one step:
var loaded = ProcessorChain.FromJson(File.ReadAllText("my-preset.json"));
// Pass loaded to AudioSession, or Initialize(sampleRate, channels) before Process.
```

`FromJson` on the configuration model checks JSON structure. Building via `ProcessorChain.FromConfiguration` or `ProcessorChain.FromJson` additionally checks effect identifiers and ranges. `ToJson` writes indented JSON using the model's current values; it does not validate effect ranges or automatically insert omitted defaults. JSON/schema failures throw `JsonException`; invalid effect configuration throws `ArgumentException`.

Each build snapshots configuration into independent processor instances. Changing the model does not modify an existing chain. Only configuration is serialized: buffers, oscillator phases, reverb tails, audio formats, and devices are not saved. Initialization/reset starts fresh state. Custom C# processors can still be used through `ProcessorChain` directly, but JSON recognizes only the built-in effect types.

## Migrating from effect flags

The CLI no longer accepts `--drive` or `--wet`. Put drive and reverb wet values into their respective JSON objects. Use `--bypass` to skip the effect chain, or load an empty preset. Every `run` invocation needs `--chain PATH` or `--bypass`; `list` and help do not need a preset.
