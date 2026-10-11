# Ring modulator

[Documentation index](../README.md) · [Preset format](../Presets.md)

Multiplies the input by a bipolar sine carrier, producing metallic or bell-like sidebands. The dry blend makes the effect less extreme.

## JSON type and parameters

Use the case-sensitive type `ringModulator`. All parameters are optional and use the defaults below. All values are finite JSON numbers; ranges are inclusive unless explicitly written with `<` or `>`.

| Parameter | Default | Range | Meaning |
| --- | --- | --- | --- |
| `frequencyHz` | 440 | 0..20000 | Carrier frequency in Hz, not a musical interval. 0 makes the wet branch silent. |
| `wet` | 0.5 | 0..1 | Linear dry/modulated mix. 0 preserves input; 1 applies full ring modulation. |

## Example preset

This complete preset uses the defaults. Save it to a JSON file and load it with `run --chain PATH`. To combine effects, copy the object inside `processors` into another preset's array.

```json
{
  "processors": [
    {
      "type": "ringModulator",
      "parameters": {
        "frequencyHz": 440,
        "wet": 0.5
      }
    }
  ]
}
```

## Behavior and placement

The carrier starts at a zero crossing on initialization/reset and stays in phase across channels and blocks. For a sine input, the wet signal contains sum and difference frequencies rather than the original tone alone. High carrier/input frequencies can alias, especially at low processing sample rates; no anti-alias filtering is applied. At frequencyHz 0 the output is the dry input multiplied by 1 - wet.

