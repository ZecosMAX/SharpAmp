using System.Globalization;

namespace SharpAmp.Cli;

internal enum CommandKind { Help, List, Run }

internal sealed record CliCommand(CommandKind Kind, RunOptions Options);

internal static class CommandLine
{
    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "--input", "--output", "--rate", "--input-channels", "--output-channels",
        "--input-offset", "--output-offset", "--latency", "--buffer", "--drive", "--wet", "--seconds"
    };

    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            return new(CommandKind.Help, new RunOptions());

        var kind = args[0] switch
        {
            "list" => CommandKind.List,
            "run" => CommandKind.Run,
            _ => throw new ArgumentException("Use 'list', 'run', or '--help'.")
        };

        var values = ReadOptions(args);
        if (kind == CommandKind.List && values.Count != 0)
            throw new ArgumentException("The list command takes no options.");

        var options = BindOptions(values);
        if (values.ContainsKey("--seconds") && options.Seconds <= 0)
            throw new ArgumentException("--seconds must be positive.");

        return new(kind, options);
    }

    private static Dictionary<string, string> ReadOptions(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i];
            var value = "true";
            if (key != "--bypass")
            {
                if (!ValueOptions.Contains(key))
                    throw new ArgumentException($"Unknown option {key}.");
                if (++i == args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Missing value for {key}.");
                value = args[i];
            }

            if (!values.TryAdd(key, value))
                throw new ArgumentException($"Duplicate option {key}.");
        }
        return values;
    }

    private static RunOptions BindOptions(IReadOnlyDictionary<string, string> values)
    {
        var defaults = new RunOptions();
        var bypass = values.ContainsKey("--bypass");
        return defaults with
        {
            InputIndex = ReadInt(values, "--input"),
            OutputIndex = ReadInt(values, "--output"),
            SampleRate = ReadInt(values, "--rate") ?? defaults.SampleRate,
            InputChannels = ReadInt(values, "--input-channels") ?? defaults.InputChannels,
            OutputChannels = ReadInt(values, "--output-channels") ?? defaults.OutputChannels,
            InputOffset = ReadInt(values, "--input-offset") ?? defaults.InputOffset,
            OutputOffset = ReadInt(values, "--output-offset") ?? defaults.OutputOffset,
            LatencyMilliseconds = ReadInt(values, "--latency") ?? defaults.LatencyMilliseconds,
            BufferMilliseconds = ReadInt(values, "--buffer") ?? defaults.BufferMilliseconds,
            Drive = bypass ? defaults.Drive : ReadFloat(values, "--drive") ?? defaults.Drive,
            Wet = bypass ? defaults.Wet : ReadFloat(values, "--wet") ?? defaults.Wet,
            Bypass = bypass,
            Seconds = ReadInt(values, "--seconds") ?? defaults.Seconds
        };
    }

    private static int? ReadInt(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
            return null;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            throw new ArgumentException($"{key} must be an integer.");
        return number;
    }

    private static float? ReadFloat(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
            return null;
        if (!float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number))
            throw new ArgumentException($"{key} must be a number using '.' as the decimal separator.");
        return number;
    }
}
