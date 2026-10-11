# Bitcrusher

[Documentation index](../README.md) · [Preset format](../Presets.md)

Combines reduced signed-PCM bit depth with sample-and-hold rate reduction for rough digital textures.

## JSON type and parameters

Use the case-sensitive type `bitcrusher`. All parameters are optional and use the defaults below. All values are finite JSON numbers; ranges are inclusive unless explicitly written with `<` or `>`.

| Parameter | Default | Range | Meaning |
| --- | --- | --- | --- |
| `bits` | 8 | integer 2..16 | Quantization resolution. Lower values produce larger amplitude steps. |
| `downsample` | 4 | integer 1..64 | Number of frames to hold each captured sample. 1 captures every frame; 4 captures every fourth frame. |
| `wet` | 1 | 0..1 | Linear dry/crushed mix. 0 preserves the input unchanged. |

## Example preset

This complete preset uses the defaults. Save it to a JSON file and load it with `run --chain PATH`. To combine effects, copy the object inside `processors` into another preset's array.

```json
{
  "processors": [
    {
      "type": "bitcrusher",
      "parameters": {
        "bits": 8,
        "downsample": 4,
        "wet": 1
      }
    }
  ]
}
```

## Behavior and placement

The wet input is clamped to ±1 before quantization. The negative limit is -1; the positive limit is `1 - 1 / 2^(bits - 1)` (127/128 at 8 bits). Each channel holds its own value, with captures synchronized by frame. At 48 kHz, downsample 4 gives an effective 12 kHz update rate, but the output stream remains 48 kHz. There is no anti-alias filter: aliasing is part of the effect. Even bits 16 with downsample 1 still quantizes, so use wet 0 for exact bypass.

