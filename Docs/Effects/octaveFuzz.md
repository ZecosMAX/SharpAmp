# Octave fuzz

[Documentation index](../README.md) · [Preset format](../Presets.md)

Full-wave rectified soft clipping followed by a 20 Hz DC blocker. Adds an octave-up fuzz character, most clearly on single notes.

## JSON type and parameters

Use the case-sensitive type `octaveFuzz`. All parameters are optional and use the defaults below. All values are finite JSON numbers; ranges are inclusive unless explicitly written with `<` or `>`.

| Parameter | Default | Range | Meaning |
| --- | --- | --- | --- |
| `drive` | 5 | > 0 | Linear boost into the soft clipper before full-wave rectification. |
| `outputGain` | 0.35 | 0..1 | Linear output level after the dry/fuzz blend. |
| `wet` | 1 | 0..1 | Fuzz proportion. 0 gives clean input scaled by outputGain; 1 gives only octave fuzz. |

## Example preset

This complete preset uses the defaults. Save it to a JSON file and load it with `run --chain PATH`. To combine effects, copy the object inside `processors` into another preset's array.

```json
{
  "processors": [
    {
      "type": "octaveFuzz",
      "parameters": {
        "drive": 5,
        "outputGain": 0.35,
        "wet": 1
      }
    }
  ]
}
```

## Behavior and placement

Rectification folds negative waveform halves upward, emphasizing even harmonics; the DC blocker removes the resulting offset. This is not a pitch-tracking or polyphonic octave shifter, so chords can sound complex. The DC blocker keeps independent history per channel, cleared by reset. There is no oversampling. The serialized type is exactly `octaveFuzz`.

