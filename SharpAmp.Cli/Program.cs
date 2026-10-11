namespace SharpAmp.Cli;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Run(args);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"SharpAmp: {error}");
            return 1;
        }
    }

    private static void Run(string[] args)
    {
        var command = CommandLine.Parse(args);
        if (command.Kind == CommandKind.Help)
        {
            Console.WriteLine(HelpText.Text);
            return;
        }

        var devices = new DeviceSelection(Console.In, Console.Out, Console.Error, Console.IsInputRedirected);
        var available = devices.ListDevices();
        if (command.Kind == CommandKind.List)
            return;

        var sessionOptions = devices.Select(available, command.Options);
        var runner = new SessionRunner(Console.Out);
        runner.Run(sessionOptions, command.Options);
    }
}
