# Distortion

[Documentation index](../README.md) · [Preset format](../Presets.md)

Soft clipping using a tanh waveshaper, with a clean-signal blend. Suitable for moderate drive and bass presets that retain some clean definition.

## JSON type and parameters

Use the case-sensitive type `distortion`. All parameters are optional and use the defaults below. All values are finite JSON numbers; ranges are inclusive unless explicitly written with `<` or `>`.

| Parameter | Default | Range | Meaning |
| --- | --- | --- | --- |
| `drive` | 3 | > 0 | Linear boost into the soft clipper; larger values increase saturation. |
| `outputGain` | 0.35 | 0..1 | Linear level applied after the clean/processed blend. |
| `wet` | 1 | 0..1 | Processed proportion: 0 is clean input, 1 is fully clipped, 0.5 is an equal blend. |

## Example preset

This complete preset uses the defaults. Save it to a JSON file and load it with `run --chain PATH`. To combine effects, copy the object inside `processors` into another preset's array.

```json
{
  "processors": [
    {
      "type": "distortion",
      "parameters": {
        "drive": 3,
        "outputGain": 0.35,
        "wet": 1
      }
    }
  ]
}
```

## Behavior and placement

The formula is `(input * (1 - wet) + tanh(input * drive) * wet) * outputGain`. At `wet: 0`, outputGain still applies. The dry signal is taken at this processor's input, after any preceding effects. No oversampling or cabinet simulation is applied.

