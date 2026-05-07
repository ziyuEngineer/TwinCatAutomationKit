namespace TwinCatAutomationKit.IntegrationTests;

/// <summary>
/// Lightweight assertion helpers for integration tests.
/// Each method throws InvalidOperationException on failure, which surfaces
/// as a FAIL line in the PASS/FAIL runner loop.
/// </summary>
internal static class IntegrationAssertEx
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }

    public static void Contains(string expectedSubstring, string actual, string message)
    {
        if (actual.IndexOf(expectedSubstring, StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidOperationException($"{message} Missing substring '{expectedSubstring}'.");
    }
}
