# Hard overdrive

[Documentation index](../README.md) · [Preset format](../Presets.md)

Multiplies the input by drive, hard-clips it to ±1, then scales it by outputGain. Produces an abrupt, aggressive clipping sound.

## JSON type and parameters

Use the case-sensitive type `hardOverdrive`. All parameters are optional and use the defaults below. All values are finite JSON numbers; ranges are inclusive unless explicitly written with `<` or `>`.

| Parameter | Default | Range | Meaning |
| --- | --- | --- | --- |
| `drive` | 5 | > 0 | Linear boost before clipping. Larger values clip more of the waveform. |
| `outputGain` | 0.35 | 0..1 | Linear level after clipping; 0 mutes. |

## Example preset

This complete preset uses the defaults. Save it to a JSON file and load it with `run --chain PATH`. To combine effects, copy the object inside `processors` into another preset's array.

```json
{
  "processors": [
    {
      "type": "hardOverdrive",
      "parameters": {
        "drive": 5,
        "outputGain": 0.35
      }
    }
  ]
}
```

## Behavior and placement

The processing formula is `clamp(input * drive, -1, 1) * outputGain`. Channels are independent. There is no tone control, dry blend, cabinet filtering, or oversampling; strong clipping can sound bright. Place gain before it to adjust the input level and delay/reverb after it for cleaner repeats.

