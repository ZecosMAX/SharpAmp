using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Configuration;

namespace SharpAmp.Avalonia.ViewModels;

/// <summary>Explicit serial cables determine the sound. Blocks and positions are independent of the path.</summary>
public sealed class ProcessorChainViewModel : ViewModelBase
{
    public const int MaxProcessors = 64;
    public const string InputId = "$input";
    public const string OutputId = "$output";
    private readonly HashSet<ProcessorViewModel> observed = [];
    private ProcessorViewModel? selectedProcessor;
    private string boardName = "Warm room";
    private string statusMessage = "Add an effect, then select a block to shape your sound.";
    private bool hasChanges;
    private bool suppressChanges;
    private IReadOnlyList<ProcessorConnection> connections = Array.Empty<ProcessorConnection>();
    private IReadOnlyList<ProcessorViewModel> connectedProcessors = Array.Empty<ProcessorViewModel>();
    private bool hasCompleteRoute;
    private string compiledFingerprint = "";

    public ObservableCollection<ProcessorViewModel> Processors { get; } = new ProcessorCollection();
    public IReadOnlyList<EffectDescriptor> AvailableEffects => EffectCatalog.All;
    public IReadOnlyList<ProcessorConnection> Connections => connections;
    public IReadOnlyList<ProcessorViewModel> ConnectedProcessors => connectedProcessors;
    public bool HasCompleteRoute => hasCompleteRoute;
    public string RoutingSummary => !HasCompleteRoute
        ? "No output path · Connect INPUT to OUTPUT to hear your sound."
        : ConnectedProcessors.Count == 0 ? "Input → output · Clean signal"
        : $"Input → output · {ConnectedProcessors.Count} effects connected";
    public bool IsEmpty => Processors.Count == 0;
    public bool HasSelection => SelectedProcessor is not null;
    public bool CanAddEffects => Processors.Count < MaxProcessors;
    public bool HasChanges { get => hasChanges; private set => SetProperty(ref hasChanges, value); }
    public string StatusMessage { get => statusMessage; set => SetProperty(ref statusMessage, value); }
    public string BoardName
    {
        get => boardName;
        set
        {
            // Keep the text as typed, including an empty field while the user replaces its name.
            var name = value ?? "";
            if (name.Length > 160) name = name[..160];
            if (!SetProperty(ref boardName, name)) return;
            OnPropertyChanged(nameof(PresetName));
            if (!suppressChanges) HasChanges = true;
        }
    }
    public string PresetName => string.IsNullOrWhiteSpace(BoardName) ? "Untitled board" : BoardName.Trim();

    public ProcessorViewModel? SelectedProcessor
    {
        get => selectedProcessor;
        set
        {
            if (value is not null && !Processors.Contains(value)) return;
            var previous = selectedProcessor;
            if (!SetProperty(ref selectedProcessor, value)) return;
            if (previous is not null) previous.IsSelected = false;
            if (value is not null) value.IsSelected = true;
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    /// <summary>Raised only when the compiled audio changes, never for selection or block positions.</summary>
    public event EventHandler? ChainChanged;

    public ProcessorChainViewModel()
    {
        Processors.CollectionChanged += OnProcessorsChanged;
        suppressChanges = true;
        Processors.Add(EffectCatalog.Find("hardOverdrive").Create());
        Processors.Add(EffectCatalog.Find("reverb").Create());
        SetConnections(CreateSerialConnections(Processors));
        Arrange();
        SelectedProcessor = Processors[0];
        suppressChanges = false;
        compiledFingerprint = GetCompiledFingerprint();
        StatusMessage = "Add a block, then connect its ports to place it in your sound.";
    }

    public ProcessorChain CreateChain() => ProcessorChain.FromConfiguration(new ProcessorChainConfiguration
    {
        Processors = GetCompiledConfigurations()
    });

    public ProcessorViewModel AddEffect(string type)
    {
        if (!CanAddEffects) throw new InvalidOperationException($"A board can contain up to {MaxProcessors} effects.");
        var processor = EffectCatalog.Find(type).Create();
        SetGridPosition(processor, Processors.Count, 3);
        Processors.Add(processor);
        SelectedProcessor = processor;
        StatusMessage = $"Added {processor.Name}. Connect its input and output to use it.";
        return processor;
    }

    public bool RemoveSelected()
    {
        if (SelectedProcessor is not { } selected) return false;
        var index = Processors.IndexOf(selected);
        Processors.Remove(selected);
        SelectedProcessor = Processors.Count == 0 ? null : Processors[Math.Min(index, Processors.Count - 1)];
        StatusMessage = $"Removed {selected.Name}.";
        return true;
    }

    public ProcessorViewModel? DuplicateSelected()
    {
        if (SelectedProcessor is not { } selected) return null;
        if (!CanAddEffects) throw new InvalidOperationException($"A board can contain up to {MaxProcessors} effects.");
        var duplicate = new ProcessorViewModel(selected.Type, selected.Name, selected.Description,
            selected.Category, selected.Accent, Guid.NewGuid().ToString("N"),
            selected.Parameters.Select(parameter => new ProcessorParameterViewModel(parameter.Key,
                parameter.Name, parameter.Minimum, parameter.Maximum, parameter.Step, parameter.Value, parameter.Unit)).ToArray());
        duplicate.IsBypassed = selected.IsBypassed;
        duplicate.X = selected.X + 24;
        duplicate.Y = selected.Y + 24;
        Processors.Insert(Processors.IndexOf(selected) + 1, duplicate);
        SelectedProcessor = duplicate;
        StatusMessage = $"Duplicated {selected.Name}. Connect its input and output to use it.";
        return duplicate;
    }

    public bool MoveSelected(int delta)
    {
        if (SelectedProcessor is not { } selected || delta == 0) return false;
        if (!selected.IsInSignalPath) return false;
        var ordered = ConnectedProcessors.ToList();
        var index = ordered.IndexOf(selected);
        var target = (int)Math.Clamp((long)index + delta, 0, ordered.Count - 1);
        if (index == target) return false;
        ordered.RemoveAt(index);
        ordered.Insert(target, selected);
        var pathIds = ConnectedProcessors.Select(processor => processor.Id).Append(InputId).ToHashSet(StringComparer.Ordinal);
        SetConnections(Connections.Where(connection => !pathIds.Contains(connection.SourceId))
            .Concat(CreateSerialConnections(ordered)).ToArray());
        SoundChanged();
        StatusMessage = $"{selected.Name} is now effect {selected.Order} of {ConnectedProcessors.Count}.";
        return true;
    }

    /// <summary>Replace only the cables occupying these two ports; other cables and block positions stay put.</summary>
    public bool Connect(ProcessorViewModel source, ProcessorViewModel target) =>
        Processors.Contains(source) && Processors.Contains(target) && Connect(source.Id, target.Id);

    public bool Connect(string sourceId, string targetId)
    {
        var next = Connections.Where(connection => connection.SourceId != sourceId && connection.TargetId != targetId)
            .Append(new ProcessorConnection(sourceId, targetId)).ToArray();
        if (!TryValidateConnections(next, Processors.Select(processor => processor.Id).ToHashSet(StringComparer.Ordinal), out var error))
        {
            StatusMessage = error;
            return false;
        }
        if (Connections.Contains(new ProcessorConnection(sourceId, targetId))) return true;
        SetConnections(next);
        SoundChanged();
        StatusMessage = $"Connected {PortName(sourceId)} to {PortName(targetId)}.";
        return true;
    }

    public bool Disconnect(string sourceId, string targetId) =>
        DisconnectWhere(connection => connection.SourceId == sourceId && connection.TargetId == targetId);

    public bool DisconnectInput(string targetId) => DisconnectWhere(connection => connection.TargetId == targetId);
    public bool DisconnectOutput(string sourceId) => DisconnectWhere(connection => connection.SourceId == sourceId);

    private bool DisconnectWhere(Func<ProcessorConnection, bool> match)
    {
        var next = Connections.Where(connection => !match(connection)).ToArray();
        if (next.Length == Connections.Count) return false;
        SetConnections(next);
        SoundChanged();
        StatusMessage = "Cable disconnected. Drag between ports to reconnect.";
        return true;
    }

    public void Arrange(int columns = 3)
    {
        columns = Math.Clamp(columns, 1, 8);
        var ordered = ConnectedProcessors.Concat(Processors.Where(processor => !processor.IsInSignalPath)).ToArray();
        for (var index = 0; index < ordered.Length; index++) SetGridPosition(ordered[index], index, columns);
        StatusMessage = "Blocks arranged. Connections are unchanged.";
    }

    public void NewBoard()
    {
        ReplaceBoard([], [new ProcessorConnection(InputId, OutputId)], "Untitled board");
        HasChanges = true;
        StatusMessage = "Your board is ready. Add an effect to get started.";
    }

    public void MarkSaved() => HasChanges = false;

    public string ToPresetJson() => JsonSerializer.Serialize(new EditorPresetDocument
    {
        Version = 2,
        Name = PresetName,
        Processors = Processors.Select(processor => new EditorProcessorDocument
        {
            Id = processor.Id,
            Type = processor.Type,
            IsBypassed = processor.IsBypassed,
            Parameters = processor.ToConfiguration().Parameters
        }).ToList(),
        Connections = Connections.ToList()
    }, EditorDocuments.JsonOptions);

    public void LoadPresetJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var document = JsonSerializer.Deserialize<EditorPresetDocument>(json, EditorDocuments.JsonOptions)
            ?? throw new JsonException("The preset must be an object.");
        if (document.Version is not null and not 1 and not 2) throw new JsonException("This preset version is not supported.");
        if (document.Version == 2 && document.Connections is null) throw new JsonException("The preset must contain a connections array.");
        if (document.Version != 2 && document.Connections is not null) throw new JsonException("Explicit connections require preset version 2.");
        if (document.Processors is null) throw new JsonException("The preset must contain a processors array.");
        if (document.Processors.Count > MaxProcessors) throw new JsonException($"A preset can contain up to {MaxProcessors} effects.");
        if (document.Name?.Length > 160) throw new JsonException("The preset name is too long.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var next = new List<ProcessorViewModel>();
        foreach (var item in document.Processors)
        {
            if (item is null) throw new JsonException("Each effect must be an object.");
            if (document.Version == 2 && item.Id is null) throw new JsonException("Each effect must have an ID.");
            var id = item.Id is null ? Guid.NewGuid().ToString("N") : ParseId(item.Id);
            if (!ids.Add(id)) throw new JsonException("Each effect must have a unique ID.");
            // Compile before creating controls: no invalid values are silently clamped, including bypassed effects.
            ProcessorChain.FromConfiguration(new ProcessorChainConfiguration
            {
                Processors = [new ProcessorConfiguration { Type = item.Type, Parameters = item.Parameters }]
            });
            var processor = EffectCatalog.Find(item.Type).Create(id, item.Parameters);
            processor.IsBypassed = item.IsBypassed;
            next.Add(processor);
        }

        var nextConnections = document.Version == 2
            ? document.Connections!.Select(connection => connection is null
                ? throw new JsonException("Each connection must be an object.")
                : new ProcessorConnection(ParsePortId(connection.SourceId), ParsePortId(connection.TargetId))).ToArray()
            : CreateSerialConnections(next);
        if (!TryValidateConnections(nextConnections, ids, out var error)) throw new JsonException(error);
        ReplaceBoard(next, nextConnections, document.Name ?? "Imported preset");
        StatusMessage = $"Loaded {PresetName}.";
    }

    public string ToLayoutJson() => JsonSerializer.Serialize(new EditorLayoutDocument
    {
        Version = 1,
        Nodes = Processors.Select(processor => new EditorNodePosition
        {
            Id = processor.Id, Type = processor.Type, X = processor.X, Y = processor.Y
        }).ToList()
    }, EditorDocuments.JsonOptions);

    public void LoadLayoutJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var document = JsonSerializer.Deserialize<EditorLayoutDocument>(json, EditorDocuments.JsonOptions)
            ?? throw new JsonException("The layout must be an object.");
        if (document.Version != 1) throw new JsonException("This layout version is not supported.");
        if (document.Nodes is null || document.Nodes.Count != Processors.Count)
            throw new JsonException("This layout is for a different number of effects. Open its matching preset first.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var positions = new List<(string Id, EditorNodePosition Position)>();
        foreach (var item in document.Nodes)
        {
            if (item is null) throw new JsonException("Each layout position must be an object.");
            var id = ParseId(item.Id);
            if (!ids.Add(id)) throw new JsonException("Each layout position must have a unique ID.");
            if (!double.IsFinite(item.X) || !double.IsFinite(item.Y) || item.X < 0 || item.Y < 0 || item.X > 100000 || item.Y > 100000)
                throw new JsonException("Block positions must be between 0 and 100000.");
            positions.Add((id, item));
        }

        var matchesIds = positions.Any(position => Processors.Any(processor => processor.Id == position.Id));
        var changes = new List<(ProcessorViewModel Processor, EditorNodePosition Position)>();
        for (var index = 0; index < positions.Count; index++)
        {
            var (id, position) = positions[index];
            // IDs take priority. An entirely new copy of the same ordered preset can reuse a layout by type.
            var processor = matchesIds ? Processors.FirstOrDefault(candidate => candidate.Id == id) : Processors[index];
            if (processor is null || processor.Type != position.Type)
                throw new JsonException("This layout does not match these effects. Open its matching preset first.");
            changes.Add((processor, position));
        }

        foreach (var (processor, position) in changes)
        {
            processor.X = position.X;
            processor.Y = position.Y;
        }
        StatusMessage = "Layout loaded. Your sound settings are unchanged.";
    }

    private void ReplaceBoard(IReadOnlyList<ProcessorViewModel> next, IReadOnlyList<ProcessorConnection> nextConnections, string name)
    {
        suppressChanges = true;
        try
        {
            Processors.Clear();
            foreach (var processor in next) Processors.Add(processor);
            SetConnections(nextConnections);
            BoardName = name;
            Arrange();
            SelectedProcessor = Processors.FirstOrDefault();
            HasChanges = false;
        }
        finally { suppressChanges = false; }
        NotifyCompiledChange();
    }

    private void OnProcessorsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        foreach (var removed in observed.Where(processor => !Processors.Contains(processor)).ToArray())
        {
            removed.PropertyChanged -= OnProcessorChanged;
            foreach (var parameter in removed.Parameters) parameter.PropertyChanged -= OnParameterChanged;
            removed.UpdateRouting(false, false, 0);
            observed.Remove(removed);
        }
        foreach (var added in Processors.Where(processor => !observed.Contains(processor)))
        {
            added.PropertyChanged += OnProcessorChanged;
            foreach (var parameter in added.Parameters) parameter.PropertyChanged += OnParameterChanged;
            observed.Add(added);
        }
        var ids = Processors.Select(processor => processor.Id).ToHashSet(StringComparer.Ordinal);
        SetConnections(Connections.Where(connection => (connection.SourceId == InputId || ids.Contains(connection.SourceId))
            && (connection.TargetId == OutputId || ids.Contains(connection.TargetId))).ToArray());
        if (SelectedProcessor is not null && !Processors.Contains(SelectedProcessor)) SelectedProcessor = null;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanAddEffects));
        SoundChanged();
    }

    private void OnProcessorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ProcessorViewModel.IsBypassed)) SoundChanged();
    }

    private void OnParameterChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ProcessorParameterViewModel.Value)) SoundChanged();
    }

    private void SoundChanged()
    {
        if (suppressChanges) return;
        HasChanges = true;
        NotifyCompiledChange();
    }

    private List<ProcessorConfiguration> GetCompiledConfigurations() => HasCompleteRoute
        ? ConnectedProcessors.Where(processor => !processor.IsBypassed)
            .Select(processor => processor.ToConfiguration()).ToList()
        : [new ProcessorConfiguration { Type = "gain", Parameters = new() { ["gain"] = 0 } }];

    private string GetCompiledFingerprint() => JsonSerializer.Serialize(GetCompiledConfigurations(), EditorDocuments.JsonOptions);

    private void NotifyCompiledChange()
    {
        var next = GetCompiledFingerprint();
        if (next == compiledFingerprint) return;
        compiledFingerprint = next;
        ChainChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetConnections(IEnumerable<ProcessorConnection> next)
    {
        var snapshot = next.ToArray();
        var changed = !Connections.SequenceEqual(snapshot);
        if (changed) connections = Array.AsReadOnly(snapshot);
        var outgoing = Connections.ToDictionary(connection => connection.SourceId, connection => connection.TargetId, StringComparer.Ordinal);
        var byId = Processors.ToDictionary(processor => processor.Id, StringComparer.Ordinal);
        var path = new List<ProcessorViewModel>();
        var cursor = InputId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(cursor) && outgoing.TryGetValue(cursor, out var target))
        {
            cursor = target;
            if (cursor == OutputId) break;
            if (!byId.TryGetValue(cursor, out var processor)) break;
            path.Add(processor);
        }
        hasCompleteRoute = cursor == OutputId;
        connectedProcessors = Array.AsReadOnly(hasCompleteRoute ? path.ToArray() : []);
        var incoming = Connections.Select(connection => connection.TargetId).ToHashSet(StringComparer.Ordinal);
        var order = ConnectedProcessors.Select((processor, index) => (processor.Id, Order: index + 1))
            .ToDictionary(item => item.Id, item => item.Order, StringComparer.Ordinal);
        foreach (var processor in Processors)
            processor.UpdateRouting(incoming.Contains(processor.Id), outgoing.ContainsKey(processor.Id), order.GetValueOrDefault(processor.Id));
        if (changed) OnPropertyChanged(nameof(Connections));
        OnPropertyChanged(nameof(ConnectedProcessors));
        OnPropertyChanged(nameof(HasCompleteRoute));
        OnPropertyChanged(nameof(RoutingSummary));
    }

    private static ProcessorConnection[] CreateSerialConnections(IEnumerable<ProcessorViewModel> processors)
    {
        var ids = processors.Select(processor => processor.Id).Prepend(InputId).Append(OutputId).ToArray();
        return ids.Zip(ids.Skip(1), (source, target) => new ProcessorConnection(source, target)).ToArray();
    }

    private static bool TryValidateConnections(IReadOnlyList<ProcessorConnection> candidate, HashSet<string> ids, out string error)
    {
        var outgoing = new Dictionary<string, string>(StringComparer.Ordinal);
        var incoming = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connection in candidate)
        {
            if (connection.SourceId is null || connection.TargetId is null ||
                (connection.SourceId != InputId && !ids.Contains(connection.SourceId)) ||
                (connection.TargetId != OutputId && !ids.Contains(connection.TargetId)))
            {
                error = "Connect an output port to an input port on this board.";
                return false;
            }
            if (!outgoing.TryAdd(connection.SourceId, connection.TargetId) || !incoming.Add(connection.TargetId))
            {
                error = "Each input and output can have only one cable.";
                return false;
            }
        }
        foreach (var source in outgoing.Keys)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var cursor = source;
            while (outgoing.TryGetValue(cursor, out var next))
            {
                if (!visited.Add(cursor))
                {
                    error = "That connection would create a loop. Connect forward through your effects.";
                    return false;
                }
                cursor = next;
            }
        }
        error = "";
        return true;
    }

    private string PortName(string id) => id == InputId ? "INPUT" : id == OutputId ? "OUTPUT"
        : Processors.First(processor => processor.Id == id).Name;

    private static string ParsePortId(string? id) => id is InputId or OutputId ? id
        : id is not null ? ParseId(id) : throw new JsonException("Each connection must have a source and a target ID.");

    private static void SetGridPosition(ProcessorViewModel processor, int index, int columns)
    {
        processor.X = 120 + index % columns * 316;
        processor.Y = 100 + index / columns * 224;
    }

    private static string ParseId(string id) => Guid.TryParse(id, out var parsed)
        ? parsed.ToString("N") : throw new JsonException("Effect IDs must be valid GUIDs.");

    private sealed class ProcessorCollection : ObservableCollection<ProcessorViewModel>
    {
        protected override void InsertItem(int index, ProcessorViewModel item)
        {
            Validate(item, -1);
            if (Count >= MaxProcessors) throw new InvalidOperationException($"A board can contain up to {MaxProcessors} effects.");
            base.InsertItem(index, item);
        }

        protected override void SetItem(int index, ProcessorViewModel item)
        {
            Validate(item, index);
            base.SetItem(index, item);
        }

        private void Validate(ProcessorViewModel item, int replacement)
        {
            ArgumentNullException.ThrowIfNull(item);
            for (var index = 0; index < Count; index++)
                if (index != replacement && this[index].Id == item.Id)
                    throw new ArgumentException("A block can only appear once in a board.", nameof(item));
        }
    }
}
