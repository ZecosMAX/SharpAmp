# Delay

[Documentation index](../README.md) · [Preset format](../Presets.md)

Clean repeating echoes with separate delay histories per channel. Use a short time for slapback or a longer time for audible repeats.

## JSON type and parameters

Use the case-sensitive type `delay`. All parameters are optional and use the defaults below. All values are finite JSON numbers; ranges are inclusive unless explicitly written with `<` or `>`.

| Parameter | Default | Range | Meaning |
| --- | --- | --- | --- |
| `timeMs` | 300 | 1..2000 | Echo spacing in milliseconds, rounded to the nearest sample with a one-sample minimum. |
| `feedback` | 0.35 | 0 <= value < 1 | Fraction recirculated into the delay. 0 gives one echo; higher values give more repeats. |
| `wet` | 0.25 | 0..1 | Linear dry/echo mix: 0 is dry and 1 is only echoes. |

## Example preset

This complete preset uses the defaults. Save it to a JSON file and load it with `run --chain PATH`. To combine effects, copy the object inside `processors` into another preset's array.

```json
{
  "processors": [
    {
      "type": "delay",
      "parameters": {
        "timeMs": 300,
        "feedback": 0.35,
        "wet": 0.25
      }
    }
  ]
}
```

## Behavior and placement

Wet output is normalized by `1 - feedback`, so increasing feedback also reduces the first echo's level. An impulse of amplitude 1 produces wet echoes of `(1 - feedback)`, `(1 - feedback) * feedback`, and so on before the wet mix is applied. There is no tempo sync or ping-pong routing. Reset and session stop clear/cut the tail.

