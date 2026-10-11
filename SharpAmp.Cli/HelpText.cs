namespace SharpAmp.Cli;

internal static class HelpText
{
    public static string Text => $$"""
        SharpAmp — live input → hard overdrive → reverb → output

        dotnet run --project SharpAmp.Cli -- list
        dotnet run --project SharpAmp.Cli -- run
        dotnet run --project SharpAmp.Cli -- run --input 0 --output 1 --drive 3 --wet 0.2
        dotnet run --project SharpAmp.Cli -- run --chain presets/overdrive-room.json
        dotnet run --project SharpAmp.Cli -- run --chain "presets/guitar_(clean).json"

        list                 List input/output device indices separately.
        run                  Select devices interactively, or use the indices from list.
        --input N            Input index (current device list).
        --output N           Output index (current device list).
        --rate N             Processing/ASIO rate in Hz (default 48000).
        --input-channels N   MME/DX/ASIO capture channels (default 1; WASAPI uses mix format).
        --output-channels N  Playback channels (default 2).
        --input-offset N     First ASIO input channel, zero based (default 0).
        --output-offset N    First ASIO output channel, zero based (default 0).
        --latency N          Requested latency in ms (default 50; ASIO uses driver settings).
        --buffer N           Capture queue capacity in ms (default 250; >= 2 * latency).
        --drive N            Overdrive drive, positive (default {{RunOptions.DefaultDrive}}).
        --wet N              Reverb wet mix, 0..1 (default 0.2).
        --chain PATH         Load a JSON chain (cannot combine with --drive, --wet, --bypass).
        --bypass             Use an empty DSP chain.
        --seconds N          Stop automatically after N seconds; otherwise Ctrl+C stops.

        DX means DirectSound. Hardware must support the selected format/channel count.
        Use headphones and start with a low output volume to avoid acoustic feedback.
        """;
}
