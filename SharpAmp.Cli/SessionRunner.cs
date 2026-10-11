using System.Diagnostics;
using SharpAmp.Core.Audio;

namespace SharpAmp.Cli;

internal sealed class SessionRunner(TextWriter writer)
{
    public void Run(AudioSessionOptions sessionOptions, RunOptions options)
    {
        using var session = new AudioSession(sessionOptions, options.CreateProcessorChain());
        using var stop = new ManualResetEventSlim();
        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };

        Console.CancelKeyPress += cancel;
        try
        {
            writer.WriteLine($"{sessionOptions.Input.Name} → {sessionOptions.Output.Name}");
            writer.WriteLine($"Capture: {session.InputFormat}; DSP: {session.ProcessingFormat}");
            writer.WriteLine("Use headphones and low output volume. Press Ctrl+C to stop.");

            session.Start();
            WaitForStop(session, stop, options.Seconds);
            session.Stop();

            var error = session.Completion.GetAwaiter().GetResult();
            writer.WriteLine($"Stopped. Dropped input frames: {session.DroppedInputFrames}.");
            if (error is not null)
                throw new InvalidOperationException("Audio session failed: " + error.Message, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private static void WaitForStop(AudioSession session, ManualResetEventSlim stop, int seconds)
    {
        var elapsed = Stopwatch.StartNew();
        while (!stop.Wait(100) && !session.Completion.IsCompleted)
        {
            if (seconds > 0 && elapsed.Elapsed.TotalSeconds >= seconds)
                return;
        }
    }
}
