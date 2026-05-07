namespace TwinCatAutomationKit.IntegrationTests;

internal sealed record IntegrationTestCase(
    string Name,
    Action Body);

internal static class IntegrationPrerequisites
{
    public static void RequireTwinCat(IntegrationTestConfig config)
    {
        if (!config.IsExplicitConfig)
        {
            throw new InvalidOperationException(
                "Config/integration-test-config.json is missing. Copy the example config and fill in this machine's values.");
        }

        if (!TwinCatEnvironmentCheck.IsAvailable(config))
        {
            throw new InvalidOperationException(
                $"TwinCAT 4026 + configured DTE/XAE are not available. Checked ProgId '{config.VisualStudioProgId}'.");
        }

        try
        {
            Directory.CreateDirectory(config.WorkRootBase);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"WorkRootBase is not writable: {config.WorkRootBase}. {ex.Message}", ex);
        }
    }

    public static void RequireActivation(IntegrationTestConfig config)
    {
        RequireTwinCat(config);
        if (!config.EnableActivation)
        {
            throw new InvalidOperationException("EnableActivation=false. Full integration coverage requires activation on a real TwinCAT target.");
        }
    }

    public static void RequireAdsRead(IntegrationTestConfig config)
    {
        RequireTwinCat(config);
        if (!config.EnableAdsRead)
        {
            throw new InvalidOperationException("EnableAdsRead=false. Full integration coverage requires ADS scan/read/read-symbols on a running target.");
        }
    }

    public static void RequireSigning(IntegrationTestConfig config)
    {
        RequireTwinCat(config);
        if (!config.EnableSigning)
        {
            throw new InvalidOperationException("EnableSigning=false. TcSignTool grant/sign/verify are excluded from default coverage unless a real OEM signing credential is configured.");
        }

        if (string.IsNullOrWhiteSpace(config.SigningCertificatePath) || !File.Exists(config.SigningCertificatePath))
        {
            throw new InvalidOperationException($"SigningCertificatePath is missing or not found: {config.SigningCertificatePath ?? "(null)"}.");
        }
    }
}
