using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;
using SharpAmp.Core.Dsp.Configuration;
using SharpAmp.Core.Dsp.Plugins;

namespace SharpAmp.Avalonia.ViewModels;

public sealed class ProcessorViewModel : ViewModelBase
{
    private bool isEditable = true;
    private bool isBypassed;
    private bool isSelected;
    private double x;
    private double y;
    private int order;
    private bool inputConnected;
    private bool outputConnected;
    private bool isInSignalPath;

    public string Id { get; }
    public string Type { get; }
    public string? PluginPath { get; internal set; }
    public string Name { get; }
    public string Description { get; }
    public string Category { get; }
    public string Accent { get; }
    public IReadOnlyList<ProcessorParameterViewModel> Parameters { get; }
    public bool IsEditable { get => isEditable; set => SetProperty(ref isEditable, value); }
    public bool IsBypassed
    {
        get => isBypassed;
        set
        {
            if (SetProperty(ref isBypassed, value)) OnPropertyChanged(nameof(StateLabel));
        }
    }
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
    public string StateLabel => IsBypassed ? "Bypassed" : "Active";
    public double X { get => x; set { if (double.IsFinite(value)) SetProperty(ref x, Math.Clamp(value, 0, 100000)); } }
    public double Y { get => y; set { if (double.IsFinite(value)) SetProperty(ref y, Math.Clamp(value, 0, 100000)); } }
    public int Order => order;
    public bool InputConnected => inputConnected;
    public bool OutputConnected => outputConnected;
    public bool IsInSignalPath => isInSignalPath;
    public string ConnectionLabel => IsInSignalPath ? "In signal path"
        : InputConnected || OutputConnected ? "Not in path" : "Unconnected";
    public string OrderLabel => IsInSignalPath ? Order.ToString("00") : "—";

    internal void UpdateRouting(bool hasInput, bool hasOutput, int signalOrder)
    {
        SetProperty(ref inputConnected, hasInput, nameof(InputConnected));
        SetProperty(ref outputConnected, hasOutput, nameof(OutputConnected));
        SetProperty(ref order, signalOrder, nameof(Order));
        SetProperty(ref isInSignalPath, signalOrder > 0, nameof(IsInSignalPath));
        OnPropertyChanged(nameof(ConnectionLabel));
        OnPropertyChanged(nameof(OrderLabel));
    }

    public ProcessorViewModel(string type, string name, string description,
        params ProcessorParameterViewModel[] parameters)
        : this(type, name, description, "Effect", "#8B9CF7", Guid.NewGuid().ToString("N"), parameters) { }

    internal ProcessorViewModel(string type, string name, string description, string category, string accent,
        string id, params ProcessorParameterViewModel[] parameters)
    {
        Type = type;
        Name = name;
        Description = description;
        Category = category;
        Accent = accent;
        Id = id;
        Parameters = Array.AsReadOnly(parameters);
    }

    public ProcessorConfiguration ToConfiguration() => new()
    {
        Type = Type,
        PluginPath = PluginPath,
        Parameters = Parameters.ToDictionary(parameter => parameter.Key, parameter => (float)parameter.Value)
    };

    internal static ProcessorViewModel CreateVst(ProcessorConfiguration configuration, string id,
        string? name = null, IReadOnlyList<VstParameter>? definitions = null)
    {
        var parameters = definitions is null
            ? configuration.Parameters.Select(pair => new ProcessorParameterViewModel(pair.Key,
                $"Parameter {pair.Key}", 0, 1, 0.001, pair.Value)).ToArray()
            : definitions.Select(parameter =>
            {
                var key = parameter.Id.ToString(CultureInfo.InvariantCulture);
                return new ProcessorParameterViewModel(key, parameter.Name, 0, 1, 0.001,
                    configuration.Parameters.GetValueOrDefault(key, parameter.DefaultValue));
            }).ToArray();
        return new ProcessorViewModel(configuration.Type,
            name ?? Path.GetFileNameWithoutExtension(configuration.PluginPath) ?? "VST effect",
            configuration.PluginPath ?? "VST effect", "VST plug-in", "#A99AE8", id, parameters)
        { PluginPath = configuration.PluginPath };
    }
}
