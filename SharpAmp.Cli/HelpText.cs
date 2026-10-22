namespace SharpAmp.Cli;

internal static class HelpText
{
    public static string Text => """
        SharpAmp — live input → JSON processor chain → output

        dotnet run --project SharpAmp.Cli -- list
        dotnet run --project SharpAmp.Cli -- run --chain presets/overdrive-room.json
        dotnet run --project SharpAmp.Cli -- run --input 0 --output 1 --chain "presets/guitar_(clean).json"
        dotnet run --project SharpAmp.Cli -- run --bypass

        list                 List input/output device indices separately.
        run                  Use --chain PATH or --bypass; select devices interactively or by index.
        --input N            Input index (current device list).
        --output N           Output index (current device list).
        --rate N             Processing/ASIO rate in Hz (default 48000).
        --input-channels N   MME/DX/ASIO capture channels (default 1; WASAPI uses mix format).
        --output-channels N  Playback channels (default 2).
        --input-offset N     First ASIO input channel, zero based (default 0).
        --output-offset N    First ASIO output channel, zero based (default 0).
        --latency N          Requested latency in ms (default 50; ASIO uses driver settings).
        --buffer N           Capture queue capacity in ms (default 250; >= 2 * latency).
        --chain PATH         JSON preset; required unless --bypass. Relative or absolute path.
        --bypass             Skip all effects. Overrides --chain without reading the file.
        --seconds N          Stop automatically after N seconds; otherwise Ctrl+C stops.

        Effects are configured only in JSON, not as command-line options.
        Effect types and parameters (default; inclusive range unless marked < or >):
          gain           gain (1; >=0, linear multiplier)
          hardOverdrive  drive (5; >0), outputGain (0.35; 0..1)
          distortion     drive (3; >0), outputGain (0.35; 0..1), wet (1; 0..1)
          reverb         wet (0.2; 0..1), feedback (0.7; 0..<1), damping (0.3; 0..<1)
          delay          timeMs (300; 1..2000), feedback (0.35; 0..<1), wet (0.25; 0..1)
          tapeDelay      timeMs (350; 1..2000), feedback (0.4; 0..<1), wet (0.25; 0..1),
                         toneHz (3000; 20..20000), wowDepthMs (2; 0..10), wowRateHz (0.5; 0..10)
          tremolo        rateHz (4; 0..20), depth (0.5; 0..1)
          bitcrusher     bits (8; integer 2..16), downsample (4; integer 1..64), wet (1; 0..1)
          octaveFuzz     drive (5; >0), outputGain (0.35; 0..1), wet (1; 0..1)
          ringModulator  frequencyHz (440; 0..20000), wet (0.5; 0..1)
          vst2 / vst3    pluginPath (absolute .dll / .vst3 path); parameters use numeric IDs (0..1)

        Names are case-sensitive. Values must be finite numbers, not strings.
        Omitted parameters use defaults. Effects run in array order; duplicates are allowed.
        Example preset: {"processors":[{"type":"gain","parameters":{"gain":0.8}}]}
        Bypass preset:  {"processors":[]}
        Save a preset as a .json file and pass its path to --chain.
        See Docs/README.md for effect references and Docs/Presets.md for the JSON format.
        See Docs/VstPlugins.md for VST compatibility and runtime insertion from C#.

        DX means DirectSound. Hardware must support the selected format/channel count.
        Use headphones and start with a low output volume to avoid acoustic feedback.
        """;
}
