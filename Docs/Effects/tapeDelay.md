# Tape delay

[Documentation index](../README.md) · [Preset format](../Presets.md)

Tape-inspired echoes with soft saturation in the feedback loop, darkening repeats, and sinusoidal delay-time wobble (wow).

## JSON type and parameters

Use the case-sensitive type `tapeDelay`. All parameters are optional and use the defaults below. All values are finite JSON numbers; ranges are inclusive unless explicitly written with `<` or `>`.

| Parameter | Default | Range | Meaning |
| --- | --- | --- | --- |
| `timeMs` | 350 | 1..2000 | Nominal echo spacing in milliseconds. |
| `feedback` | 0.4 | 0 <= value < 1 | Recirculated fraction; higher values extend the repeats. |
| `wet` | 0.25 | 0..1 | Linear dry/echo mix; the dry branch is not saturated or filtered. |
| `toneHz` | 3000 | 20..20000 | One-pole low-pass cutoff for repeats in Hz. Lower values darken the echoes. |
| `wowDepthMs` | 2 | 0..10 | Peak delay-time deviation in milliseconds. 0 disables wobble. |
| `wowRateHz` | 0.5 | 0..10 | Wobble cycles per second. 0 leaves the delay at its nominal time. |

## Example preset

This complete preset uses the defaults. Save it to a JSON file and load it with `run --chain PATH`. To combine effects, copy the object inside `processors` into another preset's array.

```json
{
  "processors": [
    {
      "type": "tapeDelay",
      "parameters": {
        "timeMs": 350,
        "feedback": 0.4,
        "wet": 0.25,
        "toneHz": 3000,
        "wowDepthMs": 2,
        "wowRateHz": 0.5
      }
    }
  ]
}
```

## Behavior and placement

The delay uses linear interpolation and stays at least one sample long, even when wow depth exceeds the nominal time. The tone cutoff is capped internally at 45% of the processing sample rate. Like delay, wet output is scaled by `1 - feedback`. Channels keep separate audio histories but share wow phase. This is a simple non-oversampled effect, not a model of a specific machine. Reset clears history and phase; stopping the session cuts repeats.

