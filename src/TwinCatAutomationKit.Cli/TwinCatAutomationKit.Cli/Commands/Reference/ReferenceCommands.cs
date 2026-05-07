using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.Core;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.Cli;

internal static class ReferenceCommands
{
    public static int ListSteps()
    {
        foreach (StepContract contract in TwinCatStepCatalog.All.OrderBy(item => item.Kind, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{contract.Kind} | {contract.MethodName} | {contract.Summary}");
        }

        return 0;
    }

    public static int ShowOrder()
    {
        foreach (string kind in TwinCatStepCatalog.RecommendedExecutionOrder)
        {
            Console.WriteLine(kind);
        }

        return 0;
    }

    public static int GenerateDocs(string[] args)
    {
        string root = RepositoryRootLocator.FindRoot();
        string outputDirectory = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Path.Combine(root, "docs", "reference");

        DocumentationSuiteWriter.WriteReferenceSuite(outputDirectory, TwinCatStepCatalog.All, TwinCatStepCatalog.RecommendedExecutionOrder);
        Console.WriteLine(outputDirectory);
        return 0;
    }
}
