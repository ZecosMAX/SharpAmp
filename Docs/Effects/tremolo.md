# Tremolo

[Documentation index](../README.md) · [Preset format](../Presets.md)

Sine-shaped volume modulation, synchronized across channels. Adds a gentle pulse or deep rhythmic dips without deliberately shifting pitch.

## JSON type and parameters

Use the case-sensitive type `tremolo`. All parameters are optional and use the defaults below. All values are finite JSON numbers; ranges are inclusive unless explicitly written with `<` or `>`.

| Parameter | Default | Range | Meaning |
| --- | --- | --- | --- |
| `rateHz` | 4 | 0..20 | Volume cycles per second. 0 holds full volume. |
| `depth` | 0.5 | 0..1 | Amount of volume reduction. The gain varies from 1 - depth to 1; 0 disables modulation, 1 reaches silence at each trough. |

## Example preset

This complete preset uses the defaults. Save it to a JSON file and load it with `run --chain PATH`. To combine effects, copy the object inside `processors` into another preset's array.

```json
{
  "processors": [
    {
      "type": "tremolo",
      "parameters": {
        "rateHz": 4,
        "depth": 0.5
      }
    }
  ]
}
```

## Behavior and placement

The oscillator starts at full volume on initialization/reset and keeps its phase across audio blocks. All channels receive the same gain. Place it before distortion to vary saturation, or after distortion to pulse the final driven sound. There is no tempo synchronization.

