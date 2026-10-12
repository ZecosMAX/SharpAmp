using SharpAmp.Avalonia.ViewModels;
using SharpAmp.Core.Dsp.Processors;

namespace SharpAmp.Tests;

public sealed class ProcessorChainViewModelTests
{
    [Fact]
    public void SlidersCreateIndependentProcessorsInDisplayOrder()
    {
        var vm = new ProcessorChainViewModel();
        vm.Processors[0].Parameters[0].Value = 7;
        vm.Processors[0].Parameters[1].Value = 0.4;
        vm.Processors[1].Parameters[0].Value = 0.25;
        vm.Processors[1].Parameters[1].Value = 0.6;
        vm.Processors[1].Parameters[2].Value = 0.5;

        var chain = vm.CreateChain();
        var drive = Assert.IsType<HardOverdriveProcessor>(chain.Processors[0]);
        var reverb = Assert.IsType<ReverbProcessor>(chain.Processors[1]);
        Assert.Equal(7, drive.Drive);
        Assert.Equal(0.4f, drive.OutputGain);
        Assert.Equal(0.25f, reverb.Wet);
        Assert.Equal(0.6f, reverb.Feedback);
        Assert.Equal(0.5f, reverb.Damping);

        vm.Processors[0].Parameters[0].Value = 9;
        Assert.Equal(7, drive.Drive);
        Assert.Equal(9, Assert.IsType<HardOverdriveProcessor>(vm.CreateChain().Processors[0]).Drive);
    }

    [Fact]
    public void AllSliderEndpointsProduceValidChainsAndNonfiniteValuesAreIgnored()
    {
        var vm = new ProcessorChainViewModel();
        foreach (var parameter in vm.Processors.SelectMany(processor => processor.Parameters))
        {
            parameter.Value = double.MinValue;
            Assert.Equal(parameter.Minimum, parameter.Value);
            vm.CreateChain().Initialize(48000, 2);
            parameter.Value = double.MaxValue;
            Assert.Equal(parameter.Maximum, parameter.Value);
            vm.CreateChain().Initialize(48000, 2);
            parameter.Value = double.NaN;
            parameter.Value = double.PositiveInfinity;
            Assert.Equal(parameter.Maximum, parameter.Value);
        }
    }
}
