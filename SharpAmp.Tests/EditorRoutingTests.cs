using System.Text.Json;
using System.Text.Json.Nodes;
using SharpAmp.Avalonia.ViewModels;
using SharpAmp.Core.Dsp.Processors;

namespace SharpAmp.Tests;

public sealed class EditorRoutingTests
{
    [Fact]
    public void DisconnectedEditsAndCollectionReorderingDoNotRebuildTheAudibleChain()
    {
        var board = new ProcessorChainViewModel();
        var first = board.Processors[0];
        var second = board.Processors[1];
        var audioChanges = 0;
        board.ChainChanged += (_, _) => audioChanges++;
        var added = board.AddEffect("gain");
        added.Parameters[0].Value = 2;
        added.IsBypassed = true;
        board.Processors.Move(0, 2);
        Assert.Equal(0, audioChanges);
        Assert.Equal(new[] { first, second }, board.ConnectedProcessors);
        Assert.True(board.HasChanges);

        first.IsBypassed = true;
        Assert.Equal(1, audioChanges);
        first.Parameters[0].Value = 7;
        Assert.Equal(1, audioChanges);
        first.IsBypassed = false;
        Assert.Equal(2, audioChanges);
        Assert.Equal(7, Assert.IsType<HardOverdriveProcessor>(board.CreateChain().Processors[0]).Drive);
    }

    [Fact]
    public void DisconnectingACableSilencesAnIncompletePathAndReconnectingRestoresIt()
    {
        var board = new ProcessorChainViewModel();
        var first = board.Processors[0];
        var second = board.Processors[1];
        var audioChanges = 0;
        var routingChanges = new List<string?>();
        board.ChainChanged += (_, _) => audioChanges++;
        board.PropertyChanged += (_, args) => routingChanges.Add(args.PropertyName);

        Assert.True(board.Disconnect(first.Id, second.Id));
        Assert.False(board.HasCompleteRoute);
        Assert.Empty(board.ConnectedProcessors);
        Assert.False(first.OutputConnected);
        Assert.True(first.InputConnected);
        Assert.Equal("—", first.OrderLabel);
        Assert.Equal("Not in path", first.ConnectionLabel);
        Assert.Equal(0, first.Order);
        Assert.False(first.IsInSignalPath);
        Assert.Contains(nameof(board.Connections), routingChanges);
        Assert.Contains(nameof(board.HasCompleteRoute), routingChanges);
        Assert.Contains(nameof(board.RoutingSummary), routingChanges);
        Assert.Equal(1, audioChanges);
        var chain = board.CreateChain();
        chain.Initialize(48000, 1);
        float[] signal = [0.3f, -0.2f];
        chain.Process(signal);
        Assert.Equal(new[] { 0f, 0f }, signal);

        Assert.False(board.Disconnect(first.Id, second.Id));
        first.Parameters[0].Value = 8;
        Assert.Equal(1, audioChanges);
        Assert.True(board.Connect(first, second));
        Assert.Equal(2, audioChanges);
        Assert.True(board.HasCompleteRoute);
        Assert.Equal("01", first.OrderLabel);
        Assert.Equal("In signal path", second.ConnectionLabel);
        Assert.Equal(8, Assert.IsType<HardOverdriveProcessor>(board.CreateChain().Processors[0]).Drive);
    }

    [Fact]
    public void DeletingABlockRemovesOnlyItsIncidentCables()
    {
        var board = new ProcessorChainViewModel();
        var first = board.Processors[0];
        var second = board.Processors[1];
        var disconnected = board.AddEffect("delay");
        var fragment = board.AddEffect("gain");
        Assert.True(board.Connect(disconnected, fragment));
        board.SelectedProcessor = first;
        Assert.True(board.RemoveSelected());
        Assert.Equal(new[]
        {
            new ProcessorConnection(second.Id, ProcessorChainViewModel.OutputId),
            new ProcessorConnection(disconnected.Id, fragment.Id)
        }, board.Connections);
        Assert.False(board.HasCompleteRoute);
        Assert.False(second.InputConnected);
        Assert.False(first.InputConnected);
        Assert.False(first.OutputConnected);
        Assert.True(board.Connect(ProcessorChainViewModel.InputId, second.Id));
        Assert.Single(board.ConnectedProcessors);
        Assert.IsType<ReverbProcessor>(Assert.Single(board.CreateChain().Processors));
    }

    [Fact]
    public void DirectInputOutputCanBeDisconnectedAndReconnectedWithLooseBlocksPresent()
    {
        var board = new ProcessorChainViewModel();
        board.NewBoard();
        var loose = board.AddEffect("gain");
        Assert.True(board.HasCompleteRoute);
        Assert.Empty(board.CreateChain().Processors);
        Assert.Equal("Unconnected", loose.ConnectionLabel);
        Assert.True(board.DisconnectInput(ProcessorChainViewModel.OutputId));
        Assert.False(board.HasCompleteRoute);
        Assert.Equal(0, Assert.IsType<GainProcessor>(Assert.Single(board.CreateChain().Processors)).Gain);
        Assert.True(board.Connect(ProcessorChainViewModel.InputId, loose.Id));
        Assert.True(board.Connect(loose.Id, ProcessorChainViewModel.OutputId));
        Assert.True(loose.IsInSignalPath);
        Assert.True(board.DisconnectOutput(ProcessorChainViewModel.InputId));
        Assert.False(board.HasCompleteRoute);
        Assert.True(board.Connect(ProcessorChainViewModel.InputId, ProcessorChainViewModel.OutputId));
        Assert.Empty(board.CreateChain().Processors);
        Assert.False(loose.InputConnected);
        Assert.False(loose.OutputConnected);
    }

    [Fact]
    public void PresetsRoundTripDisconnectedFragmentsAndCompleteRoutes()
    {
        var original = new ProcessorChainViewModel();
        var loose = original.AddEffect("gain");
        var fragment = original.AddEffect("delay");
        Assert.True(original.Connect(loose, fragment));
        var preset = original.ToPresetJson();
        Assert.Equal(2, JsonNode.Parse(preset)!["version"]!.GetValue<int>());
        var restored = new ProcessorChainViewModel();
        restored.LoadPresetJson(preset);
        Assert.Equal(preset, restored.ToPresetJson());
        Assert.Equal(2, restored.ConnectedProcessors.Count);
        Assert.Equal(4, restored.Processors.Count);
        Assert.Equal("Not in path", restored.Processors[2].ConnectionLabel);

        original.Disconnect(ProcessorChainViewModel.InputId, original.Processors[0].Id);
        restored.LoadPresetJson(original.ToPresetJson());
        Assert.Equal(original.ToPresetJson(), restored.ToPresetJson());
        Assert.False(restored.HasCompleteRoute);
    }

    [Fact]
    public void LoadingEquivalentSoundRefreshesTheBoardWithoutResettingAudio()
    {
        var board = new ProcessorChainViewModel();
        var preset = board.ToPresetJson();
        var oldFirst = board.Processors[0];
        var changes = 0;
        board.ChainChanged += (_, _) => changes++;
        board.AddEffect("gain");
        board.LoadPresetJson(preset);
        Assert.Equal(0, changes);
        Assert.NotSame(oldFirst, board.Processors[0]);
        Assert.Equal(preset, board.ToPresetJson());
        Assert.False(board.HasChanges);
    }

    [Fact]
    public void VersionOneMigratesToAnExplicitSerialPath()
    {
        var board = new ProcessorChainViewModel();
        board.LoadPresetJson("""{"version":1,"name":"Older sound","processors":[{"type":"gain"},{"type":"delay","isBypassed":true}]}""");
        Assert.Equal(board.Processors, board.ConnectedProcessors);
        Assert.Equal(3, board.Connections.Count);
        Assert.True(board.HasCompleteRoute);
        Assert.Single(board.CreateChain().Processors);
        Assert.Equal(2, JsonNode.Parse(board.ToPresetJson())!["version"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("missing-source")]
    [InlineData("missing-target")]
    [InlineData("dangling")]
    [InlineData("wrong-source")]
    [InlineData("wrong-target")]
    [InlineData("duplicate-output")]
    [InlineData("duplicate-input")]
    [InlineData("self-loop")]
    [InlineData("detached-cycle")]
    [InlineData("missing-node-id")]
    public void InvalidGraphPresetsAreRejectedBeforeAnyBoardMutation(string fault)
    {
        var board = new ProcessorChainViewModel();
        board.Processors[0].X = 799;
        var before = board.ToPresetJson();
        var layout = board.ToLayoutJson();
        var selected = board.SelectedProcessor;
        var changes = 0;
        board.PropertyChanged += (_, _) => changes++;
        board.ChainChanged += (_, _) => changes++;
        var document = JsonNode.Parse(before)!;
        var first = board.Processors[0].Id;
        var second = board.Processors[1].Id;
        JsonNode Cable(string source, string target) => JsonSerializer.SerializeToNode(new { sourceId = source, targetId = target })!;
        var cables = document["connections"]!.AsArray();
        switch (fault)
        {
            case "missing": document.AsObject().Remove("connections"); break;
            case "null": cables.Add((JsonNode?)null); break;
            case "missing-source": cables[0]!.AsObject().Remove("sourceId"); break;
            case "missing-target": cables[0]!.AsObject().Remove("targetId"); break;
            case "dangling": cables[0]!["targetId"] = Guid.NewGuid().ToString("N"); break;
            case "wrong-source": cables[0]!["sourceId"] = ProcessorChainViewModel.OutputId; break;
            case "wrong-target": cables[0]!["targetId"] = ProcessorChainViewModel.InputId; break;
            case "duplicate-output": cables.Add(Cable(first, ProcessorChainViewModel.OutputId)); break;
            case "duplicate-input": cables.Add(Cable(ProcessorChainViewModel.InputId, second)); break;
            case "self-loop": document["connections"] = new JsonArray(Cable(first, first)); break;
            case "detached-cycle": document["connections"] = new JsonArray(Cable(first, second), Cable(second, first)); break;
            case "missing-node-id": document["processors"]![0]!.AsObject().Remove("id"); break;
        }
        Assert.Throws<JsonException>(() => board.LoadPresetJson(document.ToJsonString()));
        Assert.Equal(before, board.ToPresetJson());
        Assert.Equal(layout, board.ToLayoutJson());
        Assert.Same(selected, board.SelectedProcessor);
        Assert.Equal(0, changes);
        Assert.False(board.HasChanges);
    }
}
