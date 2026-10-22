using System.Text.Json;
using System.Text.Json.Nodes;
using SharpAmp.Avalonia.ViewModels;
using SharpAmp.Core.Dsp.Processors;

namespace SharpAmp.Tests;

public sealed class EditorBoardTests
{
    [Fact]
    public void EveryCatalogEffectAndSliderEndpointCompiles()
    {
        var board = new ProcessorChainViewModel();
        board.NewBoard();
        Assert.Equal(10, board.AvailableEffects.Count);
        foreach (var effect in board.AvailableEffects)
        {
            var processor = board.AddEffect(effect.Type);
            var predecessor = board.ConnectedProcessors.LastOrDefault()?.Id ?? ProcessorChainViewModel.InputId;
            Assert.True(board.Connect(predecessor, processor.Id));
            Assert.True(board.Connect(processor.Id, ProcessorChainViewModel.OutputId));
            foreach (var parameter in processor.Parameters)
            {
                parameter.Value = parameter.Minimum;
                board.CreateChain().Initialize(48000, 2);
                parameter.Value = parameter.Maximum;
                board.CreateChain().Initialize(48000, 2);
            }
        }
        Assert.Equal(10, board.CreateChain().Processors.Count);
    }

    [Fact]
    public void AddAndDuplicateAreDisconnectedAndMovingOnlyChangesTheConnectedPath()
    {
        var board = new ProcessorChainViewModel();
        var gain = board.AddEffect("gain");
        gain.Parameters[0].Value = 1.37;
        var duplicate = Assert.IsType<ProcessorViewModel>(board.DuplicateSelected());
        Assert.NotEqual(gain.Id, duplicate.Id);
        Assert.Equal(gain.Parameters[0].Value, duplicate.Parameters[0].Value);
        Assert.NotSame(gain.Parameters[0], duplicate.Parameters[0]);
        duplicate.Parameters[0].Value = 2;
        Assert.Equal(1.37, gain.Parameters[0].Value);
        Assert.False(gain.InputConnected);
        Assert.False(gain.OutputConnected);
        Assert.False(duplicate.IsInSignalPath);
        Assert.Equal("—", duplicate.OrderLabel);
        Assert.Equal("Unconnected", duplicate.ConnectionLabel);
        Assert.False(board.MoveSelected(-3));

        duplicate.IsBypassed = true;
        Assert.Equal(2, board.CreateChain().Processors.Count);
        var first = board.Processors[0];
        var second = board.Processors[1];
        Assert.True(board.Connect(second, gain));
        Assert.True(board.Connect(gain, duplicate));
        Assert.True(board.Connect(duplicate.Id, ProcessorChainViewModel.OutputId));
        var stableOrder = board.Processors.ToArray();
        Assert.True(board.MoveSelected(-3));
        Assert.Equal(stableOrder, board.Processors);
        Assert.Equal(new[] { duplicate, first, second, gain }, board.ConnectedProcessors);
        Assert.IsType<HardOverdriveProcessor>(board.CreateChain().Processors[0]);
        Assert.True(board.RemoveSelected());
        Assert.False(board.HasCompleteRoute);
        Assert.True(board.Connect(ProcessorChainViewModel.InputId, first.Id));
        Assert.IsType<GainProcessor>(board.CreateChain().Processors[2]);
    }

    [Fact]
    public void InsertingAnEffectOnlyReplacesTheSpecifiedCablesAndNeverMovesBlocks()
    {
        var board = new ProcessorChainViewModel();
        var first = board.Processors[0];
        var second = board.Processors[1];
        var third = board.AddEffect("delay");
        var positions = board.ToLayoutJson();
        Assert.Equal(3, board.Connections.Count);
        Assert.True(board.Connect(first, third));
        Assert.False(board.HasCompleteRoute);
        Assert.Contains(new ProcessorConnection(ProcessorChainViewModel.InputId, first.Id), board.Connections);
        Assert.Contains(new ProcessorConnection(second.Id, ProcessorChainViewModel.OutputId), board.Connections);
        Assert.True(board.Connect(third.Id, second.Id));
        Assert.Equal(new[] { first, third, second }, board.ConnectedProcessors);
        Assert.Equal(new[] { first, second, third }, board.Processors);
        Assert.Equal(positions, board.ToLayoutJson());
        var before = board.ToPresetJson();
        Assert.False(board.Connect(first, first));
        Assert.False(board.Connect("missing", first.Id));
        Assert.False(board.Connect(second, first));
        Assert.False(board.Connect(ProcessorChainViewModel.OutputId, first.Id));
        Assert.False(board.Connect(second.Id, ProcessorChainViewModel.InputId));
        Assert.Equal(before, board.ToPresetJson());
        Assert.Equal(3, board.CreateChain().Processors.Count);
    }

    [Fact]
    public void AudioNotificationsExcludeSelectionAndLayoutAndDetachRemovedEffects()
    {
        var board = new ProcessorChainViewModel();
        var notifications = 0;
        board.ChainChanged += (_, _) => notifications++;
        var original = board.Processors[0];
        board.SelectedProcessor = board.Processors[1];
        original.X = 600;
        original.Y = 350;
        board.Arrange(1);
        board.LoadLayoutJson(board.ToLayoutJson());
        Assert.Equal(0, notifications);
        Assert.False(board.HasChanges);

        original.Parameters[0].Value = 6;
        original.IsBypassed = true;
        board.Processors.Remove(original);
        Assert.Equal(3, notifications);
        original.Parameters[0].Value = 7;
        original.IsBypassed = false;
        Assert.Equal(3, notifications);
        Assert.True(board.HasChanges);
        board.MarkSaved();
        Assert.False(board.HasChanges);
    }

    [Fact]
    public void PresetsPreserveIdsOrderBypassSettingsAndName()
    {
        var original = new ProcessorChainViewModel { BoardName = "Evening echoes" };
        var effect = original.AddEffect("tapeDelay");
        effect.Parameters[0].Value = 532;
        original.Processors[0].IsBypassed = true;
        original.Processors[0].X = 920;
        var json = original.ToPresetJson();
        var restored = new ProcessorChainViewModel();
        var notifications = 0;
        restored.ChainChanged += (_, _) => notifications++;
        restored.LoadPresetJson(json);

        Assert.Equal(json, restored.ToPresetJson());
        Assert.Equal(1, notifications);
        Assert.False(restored.HasChanges);
        Assert.Single(restored.CreateChain().Processors);
        Assert.IsType<ReverbProcessor>(restored.CreateChain().Processors[0]);
        Assert.Equal(120, restored.Processors[0].X); // Layout has its own document.
        Assert.Same(restored.Processors[0], restored.SelectedProcessor);
    }

    [Fact]
    public void LegacyPresetsKeepFinePrecisionAndValidValuesOutsideDefaultSliderRanges()
    {
        var board = new ProcessorChainViewModel();
        board.LoadPresetJson("""{"processors":[{"type":"gain","parameters":{"gain":8.12345}},{"type":"hardOverdrive","parameters":{"drive":0.01234}}]}""");
        Assert.Equal(8.12345f, Assert.IsType<GainProcessor>(board.CreateChain().Processors[0]).Gain);
        Assert.Equal(0.01234f, Assert.IsType<HardOverdriveProcessor>(board.CreateChain().Processors[1]).Drive);
        Assert.True(board.Processors[0].Parameters[0].Maximum >= 8.12345f);
        Assert.True(board.Processors[1].Parameters[0].Minimum <= 0.01234f);
        var saved = board.ToPresetJson();
        board.LoadPresetJson(saved);
        Assert.Equal(saved, board.ToPresetJson());
    }

    [Theory]
    [InlineData("", "Untitled board")]
    [InlineData("   ", "Untitled board")]
    [InlineData("  Evening echoes  ", "Evening echoes")]
    public void EditingTheNameRemainsUninterruptedAndSerializationDoesNotMutateIt(string text, string savedName)
    {
        var board = new ProcessorChainViewModel { BoardName = text };
        Assert.Equal(text, board.BoardName);
        Assert.True(board.HasChanges);
        Assert.Equal(savedName, board.PresetName);

        using var document = JsonDocument.Parse(board.ToPresetJson());
        Assert.Equal(savedName, document.RootElement.GetProperty("name").GetString());
        Assert.Equal(text, board.BoardName);
        Assert.True(board.HasChanges);

        board.MarkSaved();
        board.ToPresetJson();
        Assert.Equal(text, board.BoardName);
        Assert.False(board.HasChanges);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"processors\":null}")]
    [InlineData("{\"processors\":[null]}")]
    [InlineData("{\"processors\":[{\"type\":\"missing\"}]}")]
    [InlineData("{\"processors\":[{\"type\":\"delay\",\"isBypassed\":true,\"parameters\":{\"feedback\":1}}]}")]
    [InlineData("{\"processors\":[{\"type\":\"gain\",\"parameters\":{\"unknown\":1}}]}")]
    [InlineData("{\"processors\":[{\"type\":\"bitcrusher\",\"parameters\":{\"bits\":4.5}}]}")]
    [InlineData("{\"processors\":[{\"type\":\"gain\",\"parameters\":null}]}")]
    [InlineData("{\"processors\":[],\"unexpected\":true}")]
    [InlineData("{\"processors\":[],\"processors\":[]}")]
    [InlineData("{\"version\":2,\"processors\":[]}")]
    [InlineData("{\"processors\":[{\"id\":\"not-a-guid\",\"type\":\"gain\"}]}")]
    public void InvalidPresetsLeaveTheEntireExistingBoardUntouched(string json)
    {
        var board = new ProcessorChainViewModel();
        board.Processors[0].X = 725;
        var beforePreset = board.ToPresetJson();
        var beforeLayout = board.ToLayoutJson();
        var selection = board.SelectedProcessor;
        var notifications = 0;
        board.ChainChanged += (_, _) => notifications++;

        Assert.ThrowsAny<Exception>(() => board.LoadPresetJson(json));
        Assert.Equal(beforePreset, board.ToPresetJson());
        Assert.Equal(beforeLayout, board.ToLayoutJson());
        Assert.Same(selection, board.SelectedProcessor);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void DuplicatePresetIdsAndOversizedPresetsAreRejectedAtomically()
    {
        var board = new ProcessorChainViewModel();
        var document = JsonNode.Parse(board.ToPresetJson())!;
        document["processors"]![1]!["id"] = document["processors"]![0]!["id"]!.GetValue<string>();
        var before = board.ToPresetJson();
        Assert.Throws<JsonException>(() => board.LoadPresetJson(document.ToJsonString()));
        var oversized = "{\"processors\":[" + string.Join(",", Enumerable.Repeat("{\"type\":\"gain\"}", ProcessorChainViewModel.MaxProcessors + 1)) + "]}";
        Assert.Throws<JsonException>(() => board.LoadPresetJson(oversized));
        Assert.Equal(before, board.ToPresetJson());
    }

    [Fact]
    public void LayoutRestoresCoordinatesByIdentityWithoutChangingOrderOrSound()
    {
        var board = new ProcessorChainViewModel();
        var first = board.Processors[0];
        var second = board.Processors[1];
        first.X = 451;
        first.Y = 722;
        second.X = 199;
        var layout = board.ToLayoutJson();
        board.Processors.Move(0, 1);
        board.Arrange();
        var before = board.ToPresetJson();
        var notifications = 0;
        board.ChainChanged += (_, _) => notifications++;
        board.LoadLayoutJson(layout);

        Assert.Equal(451, first.X);
        Assert.Equal(722, first.Y);
        Assert.Equal(199, second.X);
        Assert.Same(second, board.Processors[0]);
        Assert.Equal(before, board.ToPresetJson());
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void AnotherCopyOfSameOrderedEffectsCanReuseLayout()
    {
        var original = new ProcessorChainViewModel();
        original.Processors[1].X = 812;
        var copy = new ProcessorChainViewModel();
        copy.LoadLayoutJson(original.ToLayoutJson());
        Assert.Equal(812, copy.Processors[1].X);
    }

    [Theory]
    [InlineData("type", "gain")]
    [InlineData("id", "not-a-guid")]
    [InlineData("x", -1)]
    [InlineData("y", 100001)]
    public void InvalidLayoutNeverPartiallyMovesBlocks(string key, object value)
    {
        var board = new ProcessorChainViewModel();
        var before = board.ToLayoutJson();
        var document = JsonNode.Parse(before)!;
        document["nodes"]![0]!["x"] = 500;
        document["nodes"]![1]![key] = JsonSerializer.SerializeToNode(value);
        Assert.Throws<JsonException>(() => board.LoadLayoutJson(document.ToJsonString()));
        Assert.Equal(before, board.ToLayoutJson());
    }

    [Fact]
    public void LayoutWithPartialIdentityMatchOrDifferentCountIsRejected()
    {
        var board = new ProcessorChainViewModel();
        var document = JsonNode.Parse(board.ToLayoutJson())!;
        document["nodes"]![1]!["id"] = Guid.NewGuid().ToString("N");
        Assert.Throws<JsonException>(() => board.LoadLayoutJson(document.ToJsonString()));
        board.AddEffect("gain");
        Assert.Throws<JsonException>(() => board.LoadLayoutJson(document.ToJsonString()));
    }

    [Fact]
    public void EmptyBoardIsPassThroughAndHasNoSelection()
    {
        var board = new ProcessorChainViewModel();
        board.NewBoard();
        Assert.True(board.IsEmpty);
        Assert.False(board.HasSelection);
        Assert.Null(board.SelectedProcessor);
        var chain = board.CreateChain();
        chain.Initialize(48000, 1);
        float[] samples = [0.25f, -0.5f];
        chain.Process(samples);
        Assert.Equal(new[] { 0.25f, -0.5f }, samples);
        Assert.False(board.RemoveSelected());
        Assert.Null(board.DuplicateSelected());
        board.LoadLayoutJson(board.ToLayoutJson());
    }

    [Fact]
    public void MaximumEffectCountIsEnforcedWithoutDroppingExistingBlocks()
    {
        var board = new ProcessorChainViewModel();
        while (board.CanAddEffects) board.AddEffect("gain");
        Assert.Equal(ProcessorChainViewModel.MaxProcessors, board.Processors.Count);
        Assert.Throws<InvalidOperationException>(() => board.AddEffect("gain"));
        Assert.Throws<InvalidOperationException>(() => board.DuplicateSelected());
        Assert.Equal(2, board.CreateChain().Processors.Count);
    }

    [Fact]
    public void ParameterEditsUseStepsAndReadableUnits()
    {
        var board = new ProcessorChainViewModel();
        var crusher = board.AddEffect("bitcrusher");
        crusher.Parameters[0].Value = 7.6;
        Assert.Equal(8, crusher.Parameters[0].Value);
        Assert.Equal("8 bit", crusher.Parameters[0].DisplayValue);
        crusher.Parameters[2].Value = 0.349;
        Assert.Equal(0.35, crusher.Parameters[2].Value);
        Assert.Equal("35%", crusher.Parameters[2].DisplayValue);
        Assert.True(board.Connect(board.Processors[1], crusher));
        Assert.True(board.Connect(crusher.Id, ProcessorChainViewModel.OutputId));
        Assert.Equal(8, Assert.IsType<BitcrusherProcessor>(board.CreateChain().Processors[2]).Bits);
    }
}
