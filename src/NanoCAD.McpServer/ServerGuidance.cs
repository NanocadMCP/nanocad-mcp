namespace NanoCAD.McpServer;

public static class ServerGuidance
{
    public const string Version = "0.249.0";
    // Product instructions are embedded, versioned and independent of repository AGENTS.md.
    public static string Instructions { get; } = ReadInstructions();

    private static string ReadInstructions()
    {
        using var stream = typeof(ServerGuidance).Assembly.GetManifestResourceStream("NanoCAD.McpServer.EngineeringInstructions")
            ?? throw new InvalidOperationException("Embedded engineering instructions are missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
