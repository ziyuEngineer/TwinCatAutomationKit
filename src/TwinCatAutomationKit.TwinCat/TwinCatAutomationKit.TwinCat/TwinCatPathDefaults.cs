namespace TwinCatAutomationKit.TwinCat;

public static class TwinCatPathDefaults
{
    public const string DefaultVisualStudioProgId = "VisualStudio.DTE.17.0";

    public const string DefaultXaeTemplatePath =
        @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj";

    public const string DefaultTcSignToolPath =
        @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\SDK\Bin\TcSignTool.exe";

    public static IReadOnlyList<string> DefaultXaeTemplatePaths { get; } = new[]
    {
        DefaultXaeTemplatePath
    };

    public static IReadOnlyList<string> DefaultPlcTemplatePaths { get; } = new[]
    {
        @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Plc\PlcTemplate\Plc Templates\Standard PLC Template.plcproj",
        @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Plc\PlcTemplate\Plc Templates\Empty PLC Template.plcproj"
    };

    public static IReadOnlyList<string> DefaultPlcWizardIds { get; } = new[]
    {
        "TcPlcProjectWizard",
        "TcPlcStandardProjectWizard",
        "TcPlcEmptyProjectWizard"
    };
}
