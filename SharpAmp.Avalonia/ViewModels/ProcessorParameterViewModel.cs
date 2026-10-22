using System;
using System.Globalization;

namespace SharpAmp.Avalonia.ViewModels;

public sealed class ProcessorParameterViewModel : ViewModelBase
{
    private double value;
    public string Key { get; }
    public string Name { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    public string Unit { get; }
    public string ValueFormat => Step < 0.01 ? "0.###" : "0.##";

    public string DisplayValue => Unit switch
    {
        "%" => (Value * 100).ToString("0.#", CultureInfo.CurrentCulture) + "%",
        "Hz" when Value >= 1000 => (Value / 1000).ToString("0.##", CultureInfo.CurrentCulture) + " kHz",
        "×" => Value.ToString("0.##", CultureInfo.CurrentCulture) + "×",
        "" => Value.ToString(ValueFormat, CultureInfo.CurrentCulture),
        _ => Value.ToString("0.##", CultureInfo.CurrentCulture) + " " + Unit
    };

    public double Value
    {
        get => value;
        set
        {
            if (!double.IsFinite(value)) return;
            var clamped = Math.Clamp(value, Minimum, Maximum);
            var stepped = Minimum + Math.Round((clamped - Minimum) / Step, MidpointRounding.AwayFromZero) * Step;
            SetValue(Math.Clamp(Math.Round(stepped, 8), Minimum, Maximum));
        }
    }

    public ProcessorParameterViewModel(string key, string name, double minimum, double maximum,
        double step, double value, string unit = "")
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
            throw new ArgumentException("Parameter limits must be finite and ordered.");
        if (!double.IsFinite(step) || step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        Key = key;
        Name = name;
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        Unit = unit;
        SetPresetValue(value);
    }

    // A preset can contain precision finer than the slider step. Preserve it until the user edits it.
    internal void SetPresetValue(double value)
    {
        if (!double.IsFinite(value) || value < Minimum || value > Maximum)
            throw new ArgumentOutOfRangeException(nameof(value));
        SetValue(value);
    }

    private void SetValue(double next)
    {
        if (SetProperty(ref value, next, nameof(Value))) OnPropertyChanged(nameof(DisplayValue));
    }
}
