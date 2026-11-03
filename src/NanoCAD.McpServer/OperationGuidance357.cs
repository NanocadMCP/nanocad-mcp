namespace NanoCAD.McpServer;

// Tool-specific operating prose is transmitted only for an explicit help read.
// Names come from the published tool allowlist; CAD content is never a topic.
internal static class OperationGuidance357
{
    internal static string Read(string operation)
    {
        if(!ToolCatalog.OperationNames.Contains(operation,StringComparer.Ordinal))
            throw new InvalidOperationException("Unknown operating-help tool.");
        using var stream=typeof(ServerGuidance).Assembly.GetManifestResourceStream("NanoCAD.McpServer.Guidance.operations")
            ??throw new InvalidOperationException("Embedded operation help is missing.");
        using var reader=new StreamReader(stream);
        string source=reader.ReadToEnd().Replace("\r\n","\n",StringComparison.Ordinal);
        string heading="# "+operation+"\n";
        int begin=source.IndexOf(heading,StringComparison.Ordinal);
        if(begin<0||(begin>0&&source[begin-1]!='\n'))throw new InvalidOperationException("Published tool operating help is missing.");
        begin+=heading.Length;
        int end=source.IndexOf("\n# ",begin,StringComparison.Ordinal);
        string text=(end<0?source[begin..]:source[begin..end]).Trim();
        if(text.Length==0)throw new InvalidOperationException("Published tool operating help is empty.");
        return text;
    }
}
