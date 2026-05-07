namespace TwinCatAutomationKit.Cli;

internal static class CliOptionParser
{
    public static Dictionary<string, string> Parse(IEnumerable<string> args)
    {
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
        foreach (string arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int separatorIndex = arg.IndexOf('=');
            if (separatorIndex < 0)
            {
                options[arg[2..]] = "true";
                continue;
            }

            options[arg[2..separatorIndex]] = arg[(separatorIndex + 1)..];
        }

        return options;
    }

    public static string? GetOption(
        IReadOnlyDictionary<string, string> options,
        params string[] keys)
    {
        foreach (string key in keys)
        {
            if (options.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    public static string RequireOption(
        IReadOnlyDictionary<string, string> options,
        params string[] keys)
    {
        string? value = GetOption(options, keys);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"Missing required option: {string.Join(" or ", keys.Select(key => "--" + key))}");
    }

    public static int GetIntOption(
        IReadOnlyDictionary<string, string> options,
        string key,
        int fallback)
    {
        string? raw = GetOption(options, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!int.TryParse(raw, out int value))
        {
            throw new InvalidOperationException($"--{key} must be an integer. Actual='{raw}'.");
        }

        return value;
    }

    public static bool GetBoolOption(
        IReadOnlyDictionary<string, string> options,
        string key,
        bool fallback)
    {
        string? raw = GetOption(options, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!bool.TryParse(raw, out bool value))
        {
            throw new InvalidOperationException($"--{key} must be true or false. Actual='{raw}'.");
        }

        return value;
    }
}
