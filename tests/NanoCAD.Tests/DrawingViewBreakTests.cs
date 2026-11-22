using NanoCAD.Contracts;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using NanoCAD.McpServer;

internal static class DrawingViewBreakTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test, Func<string, Func<Task>, Task>? hostTest = null)
    {
        await (hostTest ?? test)("drawing break known SDK getters are field-only without native calls", () =>
        {
            string managed = Environment.GetEnvironmentVariable("NanoCadManagedDir") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nanosoft AS", "nanoCAD x64 26.0", "bin");
            using var assembly = File.OpenRead(Path.Combine(managed, "..", "bin_nPlat", "mapinet.dll"));
            const string reviewedHash = "AE5AF483BE83FAA9AE826379A1C4AC689D0B097F7780AFC5D98FB3A05868CA75";
            if (Convert.ToHexString(SHA256.HashData(assembly)) != reviewedHash)
                throw new Exception("Installed custom class changed; getter review is stale.");
            assembly.Position = 0;
            using var pe = new PEReader(assembly);
            var metadata = pe.GetMetadataReader();
            var type = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition).Single(t =>
                metadata.StringComparer.Equals(t.Namespace, "Mapinet") && metadata.StringComparer.Equals(t.Name, "Break2d"));
            foreach (string name in new[] { "get_BreakType", "get_BreakTypeIndex", "get_GapBetweenLines", "get_VectSkew", "get_TargetDrawingViewId" })
            {
                var method = type.GetMethods().Select(metadata.GetMethodDefinition).Single(m => metadata.StringComparer.Equals(m.Name, name));
                var bytes = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                if (bytes is not { Length: 7 } || bytes[0] != 0x02 || bytes[1] != 0x7B || bytes[6] != 0x2A)
                    throw new Exception("Getter is not reviewed ldarg.0/ldfld/ret: " + name);
            }
            return Task.CompletedTask;
        });
        await test("drawing break catalog distinguishes unavailable details and opaque native parameters", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.get_drawing_views"); var output = tool.OutputSchema!.Value.GetRawText();
            foreach (var field in new[] { "attached_break_ids", "breaks_unavailable_reason", "adapter_schema", "class_id", "gap_between_lines", "skew_vector", "target_view_id", "parameter_space" })
                if (!output.Contains('"' + field + '"')) throw new Exception("Missing read contract: " + field);
            var help = AuditServerContractSupport.Help("get_drawing_views");
            if (!help.Contains("breaks:null") || !help.Contains("native_break_parameters") || !help.Contains("No dynamic-property discovery")) throw new Exception("Unknown custom-class boundary omitted from focused guidance.");
            if (tool.InputSchema.GetRawText().Contains("gap_between_lines")) throw new Exception("Read capability exposes a mutation field.");
            return Task.CompletedTask;
        });
    }
}

