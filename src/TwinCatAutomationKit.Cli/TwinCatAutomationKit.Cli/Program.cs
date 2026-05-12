namespace TwinCatAutomationKit.Cli;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine(CliHelpText.BuildGeneralHelp());
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "help" => Help(args.Skip(1).ToArray()),
            "list-steps" => ReferenceCommands.ListSteps(),
            "show-order" => ReferenceCommands.ShowOrder(),
            "generate-docs" => ReferenceCommands.GenerateDocs(args),
            "ads-read" => AdsReadCommand.Run(args.Skip(1).ToArray()),
            "guided-build-plan" => GuidedBuildCommand.ShowPlan(args.Skip(1).ToArray()),
            "guided-build" => GuidedBuildCommand.Run(args.Skip(1).ToArray()),
            "invoke-step" => StepInvokeCommand.Run(args.Skip(1).ToArray()),
            "run-plan" => JsonPlanCommand.Run(args.Skip(1).ToArray()),
            _ => UnknownCommand(args[0])
        };
    }

    private static int Help(string[] args)
    {
        Console.WriteLine(CliHelpText.BuildHelp(args));
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        Console.WriteLine(CliHelpText.BuildGeneralHelp());
        return 1;
    }
}
