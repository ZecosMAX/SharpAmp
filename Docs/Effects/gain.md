# Gain

[Documentation index](../README.md) · [Preset format](../Presets.md)

Scales signal amplitude without clipping or adding coloration. Use it before drive effects as a clean input boost, or after effects as an output trim.

## JSON type and parameters

Use the case-sensitive type `gain`. All parameters are optional and use the defaults below. All values are finite JSON numbers; ranges are inclusive unless explicitly written with `<` or `>`.

| Parameter | Default | Range | Meaning |
| --- | --- | --- | --- |
| `gain` | 1 | >= 0 | Linear amplitude multiplier: 0 mutes, 1 is unity, 2 is approximately +6 dB, and 0.5 is approximately -6 dB. |

## Example preset

This complete preset uses the defaults. Save it to a JSON file and load it with `run --chain PATH`. To combine effects, copy the object inside `processors` into another preset's array.

```json
{
  "processors": [
    {
      "type": "gain",
      "parameters": {
        "gain": 1
      }
    }
  ]
}
```

## Behavior and placement

Gain can produce samples above ±1 internally. A following distortion stage can clip them; the final audio output also clips to ±1. This is a level control, not a model of an analog preamp.

