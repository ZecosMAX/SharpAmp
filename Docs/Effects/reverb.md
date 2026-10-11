# Reverb

[Documentation index](../README.md) · [Preset format](../Presets.md)

A small algorithmic room-style ambience built from four parallel damped feedback combs per channel.

## JSON type and parameters

Use the case-sensitive type `reverb`. All parameters are optional and use the defaults below. All values are finite JSON numbers; ranges are inclusive unless explicitly written with `<` or `>`.

| Parameter | Default | Range | Meaning |
| --- | --- | --- | --- |
| `wet` | 0.2 | 0..1 | Ambience proportion: 0 is dry, 1 is only the reverb tail. |
| `feedback` | 0.7 | 0 <= value < 1 | Recirculated signal fraction. Higher values produce longer tails; this is not a decay time in seconds. |
| `damping` | 0.3 | 0 <= value < 1 | High-frequency smoothing in the feedback loop. Higher values darken repeats; this is a coefficient, not a frequency in Hz. |

## Example preset

This complete preset uses the defaults. Save it to a JSON file and load it with `run --chain PATH`. To combine effects, copy the object inside `processors` into another preset's array.

```json
{
  "processors": [
    {
      "type": "reverb",
      "parameters": {
        "wet": 0.2,
        "feedback": 0.7,
        "damping": 0.3
      }
    }
  ]
}
```

## Behavior and placement

The four delay times are approximately 29.7, 37.1, 41.1, and 43.7 ms, rounded to samples. Wet output is scaled by `(1 - feedback) / 4`, limiting buildup for sustained bounded input. Raising feedback also lowers the initial wet level. Channels have separate histories, so this does not create stereo width from identical inputs. Reset and session stop cut the tail. It is neither convolution nor a physical room model.

