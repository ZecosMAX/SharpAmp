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
    public string DisplayValue => Value.ToString("0.00", CultureInfo.CurrentCulture);

    public double Value
    {
        get => value;
        set
        {
            if (!double.IsFinite(value)) return;
            if (SetProperty(ref this.value, Math.Clamp(value, Minimum, Maximum)))
                OnPropertyChanged(nameof(DisplayValue));
        }
    }

    public ProcessorParameterViewModel(string key, string name, double minimum, double maximum,
        double step, double value)
    {
        Key = key;
        Name = name;
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        Value = value;
    }
}
