using System.Text;
using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.Cli;

public static class CliHelpText
{
    public static string BuildHelp(string[] args)
    {
        if (args.Length == 0)
        {
            return BuildGeneralHelp();
        }

        string topic = args[0].ToLowerInvariant();
        Dictionary<string, string> options = CliOptionParser.Parse(args.Skip(1));

        return topic switch
        {
            "invoke-step" => BuildInvokeStepHelp(),
            "run-plan" => BuildRunPlanHelp(),
            "step" => BuildStepHelp(options),
            _ => BuildGeneralHelp()
        };
    }

    public static string BuildGeneralHelp()
    {
        StringBuilder text = new();
        text.AppendLine("TwinCatAutomationKit CLI");
        text.AppendLine("Commands:");
        text.AppendLine("  help");
        text.AppendLine("  help invoke-step");
        text.AppendLine("  help run-plan");
        text.AppendLine("  help step --kind=tsproj.ensure-mapping-link");
        text.AppendLine("  list-steps");
        text.AppendLine("  show-order");
        text.AppendLine("  generate-docs [outputDirectory]");
        text.AppendLine("  ads-read --net-id=127.0.0.1.1.1 --port=851 --symbol=MAIN.bValue --type=Boolean");
        text.AppendLine("  guided-build-plan [--output=path] [--solution-name=GuidedTwinCat] [--project-name=GuidedTwinCat] [--cpp-project-name=CppBase] [--plc-project-name=PlcBase]");
        text.AppendLine("  guided-build [--output=path] [--solution-name=GuidedTwinCat] [--project-name=GuidedTwinCat] [--cpp-project-name=CppBase] [--plc-project-name=PlcBase] [--cpp-instance-count=3] [--plc-instance-count=3] [--visible=false] [--startup-delay-ms=8000] [--build=true] [--activate=false] [--auto]");
        text.AppendLine("  run-plan --file=examples\\json-plans\\complex-full-project.json [--dry-run=true] [--summary=D:\\t\\plan-summary.json]");
        text.AppendLine("  invoke-step --kind=engineering.create-module --solution-path=D:\\t\\demo\\Demo.sln --cpp-project-name=CppBase --module-name=PipelineModule");
        text.AppendLine("  invoke-step --kind=tsproj.ensure-mapping-link --solution-path=D:\\t\\demo\\Demo.sln --owner-a-name=TIXC^Demo^CppInst01 --owner-b-name=TIPC^PlcBase^PlcInst01 --var-a=Outputs.Var1 --var-b=MAIN.nValue");
        text.AppendLine("  invoke-step --kind=engineering.create-xae-solution --solution-directory=D:\\t\\demo_manual --solution-name=DemoManual --project-name=DemoManual");
        text.AppendLine();
        text.AppendLine("Idea:");
        text.AppendLine("  `guided-build` is still the fixed full-project recipe.");
        text.AppendLine("  `invoke-step` is the flexible per-interface entry point for reusing the same public operations across many existing projects.");
        return text.ToString().TrimEnd();
    }

    public static string BuildRunPlanHelp()
    {
        StringBuilder text = new();
        text.AppendLine("run-plan");
        text.AppendLine("Purpose:");
        text.AppendLine("  Execute a JSON file containing multiple invoke-step operations.");
        text.AppendLine();
        text.AppendLine("Options:");
        text.AppendLine("  --file=...             Required JSON plan path. Alias: --plan=...");
        text.AppendLine("  --dry-run=true         Resolve variables and print step options without executing TwinCAT/XAE work.");
        text.AppendLine("  --stop-on-failure=true Stop after the first failed step. Default: true.");
        text.AppendLine("  --command-timeout-ms=... Wall-clock timeout for each step; a timeout always stops the plan.");
        text.AppendLine("  --reuse-engineering-session=true Keep one VS/XAE DTE session for adjacent engineering/cpp steps.");
        text.AppendLine("  --summary=...          Optional JSON summary output path.");
        text.AppendLine("  --var:name=value       Override a plan variable, for example --var:root=D:\\t\\run1.");
        text.AppendLine("  --var=name=value;...   Override multiple variables in one option.");
        text.AppendLine();
        text.AppendLine("Plan features:");
        text.AppendLine("  variables              Reusable strings such as root, solutionPath, projectPath.");
        text.AppendLine("  defaults               Options merged into every step unless overridden.");
        text.AppendLine("  files                  Inline XML/JSON payloads written before steps run.");
        text.AppendLine("  steps                  Ordered invoke-step calls with id, kind, and options.");
        text.AppendLine("  ${name}                Variable interpolation.");
        text.AppendLine("  ${steps.id.outputs.x}  Reference an earlier step output, such as a task objectId.");
        text.AppendLine();
        text.AppendLine("Examples:");
        text.AppendLine("  run-plan --file=examples\\json-plans\\complex-full-project.json --dry-run=true");
        text.AppendLine("  run-plan --file=examples\\json-plans\\complex-full-project.json --dry-run --var:root=D:\\t\\complex-run");
        text.AppendLine("  run-plan --file=examples\\json-plans\\complex-full-project.json --summary=D:\\t\\complex-plan-summary.json");
        text.AppendLine("  run-plan --file=examples\\json-plans\\complex-full-project.json --reuse-engineering-session=true --summary=D:\\t\\complex-plan-summary.json");
        text.AppendLine();
        text.AppendLine("Documentation:");
        text.AppendLine("  docs\\cli\\json-plan.md");
        return text.ToString().TrimEnd();
    }

    public static string BuildInvokeStepHelp()
    {
        StringBuilder text = new();
        text.AppendLine("invoke-step");
        text.AppendLine("Purpose:");
        text.AppendLine("  Execute one public interface at a time with explicit parameters.");
        text.AppendLine();
        text.AppendLine("Workspace options:");
        text.AppendLine("  --solution-path=...   Existing .sln path. Can be used to auto-locate the .tsproj.");
        text.AppendLine("  --project-path=...    Existing .tsproj path. Alias: --tsproj-path=...");
        text.AppendLine("  --project-name=...    Optional hint when one solution contains multiple .tsproj files.");
        text.AppendLine();
        text.AppendLine("Structured payload options:");
        text.AppendLine("  --xml-file=...        Preferred for XML fragment/section steps.");
        text.AppendLine("  --json-file=...       Preferred for batch-plan steps.");
        text.AppendLine();
        text.AppendLine("Engineering-session options:");
        text.AppendLine("  --visible=false");
        text.AppendLine("  --startup-delay-ms=8000");
        text.AppendLine("  --suppress-ui=true");
        text.AppendLine("  --enable-dialog-auto-dismiss=true Watch newly launched VS/TcXaeShell for modal prompts.");
        text.AppendLine("  --dialog-poll-interval-ms=500");
        text.AppendLine("  --attach-to-existing=false Avoid attaching to stale VS/TcXaeShell sessions in unattended runs.");
        text.AppendLine("  --command-timeout-ms=... Wall-clock timeout for this step.");
        text.AppendLine();
        text.AppendLine("Supported step kinds:");
        foreach (string kind in StepInvocationCatalog.SupportedKinds)
        {
            StepContract contract = TwinCatStepCatalog.Require(kind);
            text.AppendLine($"  {kind}");
            text.AppendLine($"    {contract.MethodName}");
            text.AppendLine($"    {contract.Summary}");
        }

        text.AppendLine();
        text.AppendLine("Examples:");
        text.AppendLine("  help step --kind=engineering.create-module");
        text.AppendLine("  invoke-step --kind=engineering.create-xae-solution --solution-directory=D:\\t\\demo_manual --solution-name=DemoManual --project-name=DemoManual");
        text.AppendLine("  invoke-step --kind=engineering.create-cpp-project --solution-path=D:\\t\\demo_manual\\DemoManual.sln --cpp-project-name=CppBase");
        text.AppendLine("  invoke-step --kind=engineering.create-plc-project --solution-path=D:\\t\\demo_manual\\DemoManual.sln --plc-project-name=PlcBase");
        text.AppendLine("  invoke-step --kind=engineering.create-module --solution-path=D:\\t\\demo\\Demo.sln --cpp-project-name=CppBase --module-name=PipelineModule");
        text.AppendLine("  invoke-step --kind=tsproj.ensure-parameter --solution-path=D:\\t\\demo\\Demo.sln --instance-name=CppInst01 --parameter-name=Parameter.data1 --value-text=10");
        text.AppendLine("  invoke-step --kind=tsproj.ensure-mapping-link --solution-path=D:\\t\\demo\\Demo.sln --owner-a-name=TIXC^Demo^CppInst01 --owner-b-name=TIPC^PlcBase^PlcInst01 --var-a=Outputs.Var1 --var-b=MAIN.nValue");
        text.AppendLine("  invoke-step --kind=validation.mark-event-log-window --marker-file=D:\\t\\demo\\tcsysrv-marker.json");
        text.AppendLine("  invoke-step --kind=validation.assert-event-log-window --marker-file=D:\\t\\demo\\tcsysrv-marker.json --fail-message-contains=\"AdsError: 1792;FPU invalid operation\"");
        text.AppendLine("  invoke-step --kind=validation.ads-read-symbols --net-id=local --port=851 \"--symbols=MAIN.nSeed:UInt32;MAIN.bPipelineOk:Boolean\"");
        return text.ToString().TrimEnd();
    }

    public static string BuildStepHelp(IReadOnlyDictionary<string, string> options)
    {
        string kind = CliOptionParser.RequireOption(options, "kind");
        StepContract contract = TwinCatStepCatalog.Require(kind);
        StringBuilder text = new();
        text.AppendLine($"Step: {contract.Kind}");
        text.AppendLine($"Method: {contract.MethodName}");
        text.AppendLine($"Category: {contract.Category}");
        text.AppendLine($"Summary: {contract.Summary}");
        text.AppendLine("Preconditions:");
        foreach (string item in contract.Preconditions)
        {
            text.AppendLine($"  - {item}");
        }

        text.AppendLine("Inputs:");
        foreach (StepParameterContract input in contract.Inputs)
        {
            string required = input.Required ? "required" : "optional";
            string example = string.IsNullOrWhiteSpace(input.Example) ? string.Empty : $" example={input.Example}";
            text.AppendLine($"  - {input.Name} ({input.Type}, {required}){example}");
            text.AppendLine($"    {input.Description}");
        }

        text.AppendLine("Outputs:");
        foreach (StepOutputContract output in contract.Outputs)
        {
            text.AppendLine($"  - {output.Name} ({output.Type})");
            text.AppendLine($"    {output.Description}");
        }

        text.AppendLine("Verification:");
        foreach (string item in contract.VerificationNotes)
        {
            text.AppendLine($"  - {item}");
        }

        if (StepInvocationCatalog.Supports(kind))
        {
            text.AppendLine("CLI:");
            text.AppendLine("  This step is supported by `invoke-step`.");
        }
        else
        {
            text.AppendLine("CLI:");
            text.AppendLine("  This step is not yet wired into `invoke-step`.");
        }

        return text.ToString().TrimEnd();
    }
}
