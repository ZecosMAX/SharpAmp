using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SharpAmp.Core.Dsp.Configuration;

namespace SharpAmp.Avalonia.ViewModels;

public sealed partial class ProcessorViewModel(string type, string name, string description,
    params ProcessorParameterViewModel[] parameters) : ViewModelBase
{
    [ObservableProperty] private bool isEditable = true;

    public string Name { get; } = name;
    public string Description { get; } = description;
    public IReadOnlyList<ProcessorParameterViewModel> Parameters { get; } = parameters;

    public ProcessorConfiguration ToConfiguration() => new()
    {
        Type = type,
        Parameters = Parameters.ToDictionary(parameter => parameter.Key, parameter => (float)parameter.Value)
    };
}
