using System.Text.RegularExpressions;
using SharpAmp.Cli;
using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Configuration;

namespace SharpAmp.Tests;

public sealed class DocumentationTests
{
    [Fact]
    public void AllDocumentationJsonExamplesCanBeLoadedAndRoundTripped()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Docs");
        var files = Directory.GetFiles(directory, "*.md", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var examples = files.SelectMany(path => Regex.Matches(File.ReadAllText(path),
            @"```json\s*\r?\n(.*?)```", RegexOptions.Singleline).Select(match => match.Groups[1].Value)).ToArray();
        Assert.NotEmpty(examples);
        foreach (var json in examples)
        {
            var preset = ProcessorChainConfiguration.FromJson(json);
            var original = ProcessorChain.FromJson(json);
            var restored = ProcessorChain.FromJson(preset.ToJson());
            original.Initialize(48000, 2);
            restored.Initialize(48000, 2);
            float[] first = [0.2f, -0.4f, 0.8f, 0.1f];
            var second = (float[])first.Clone();
            original.Process(first);
            restored.Process(second);
            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void EveryBuiltInEffectHasAPageAndAppearsInCliHelp()
    {
        var documentedTypes = new HashSet<Type>();
        var directory = Path.Combine(AppContext.BaseDirectory, "Docs", "Effects");
        foreach (var path in Directory.GetFiles(directory, "*.md"))
        {
            var content = File.ReadAllText(path);
            var example = Assert.Single(Regex.Matches(content, @"```json\s*\r?\n(.*?)```", RegexOptions.Singleline));
            var preset = ProcessorChainConfiguration.FromJson(example.Groups[1].Value);
            var effect = Assert.Single(preset.Processors);
            Assert.Equal(Path.GetFileNameWithoutExtension(path), effect.Type);
            var helpLine = Regex.Match(HelpText.Text, @"(?m)^  " + Regex.Escape(effect.Type) + @"\s+[^\r\n]*(?:\r?\n {17,}[^\r\n]+)*");
            Assert.True(helpLine.Success, $"Missing help entry for {effect.Type}");
            foreach (var parameter in effect.Parameters.Keys)
            {
                Assert.Contains($"`{parameter}`", content);
                Assert.Contains(parameter + " (", helpLine.Value);
            }
            var processor = Assert.Single(ProcessorChain.FromConfiguration(preset).Processors);
            Assert.True(documentedTypes.Add(processor.GetType()));
        }

        var builtIns = typeof(ProcessorChain).Assembly.GetTypes().Where(type =>
            type.Namespace == "SharpAmp.Core.Dsp.Processors" && !type.IsAbstract &&
            typeof(IDigitalSignalProcessor).IsAssignableFrom(type));
        Assert.True(documentedTypes.SetEquals(builtIns), "Every built-in processor needs a documentation page.");
    }

    [Theory]
    [InlineData("custom-chain.json")]
    [InlineData("bypass.json")]
    public void StandaloneExampleFilesLoadThroughCli(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Docs", "Examples", name);
        var chain = CommandLine.Parse(["run", "--chain", path]).Options.CreateProcessorChain();
        chain.Initialize(48000, 1);
        float[] samples = [0.25f, -0.5f];
        chain.Process(samples);
        Assert.All(samples, value => Assert.True(float.IsFinite(value)));
    }
}
