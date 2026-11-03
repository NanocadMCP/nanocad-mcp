using NanoCAD.Contracts;
using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.McpServer;

internal static class DrawingViewBreakTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("drawing break known SDK getters are field-only without native calls",()=>
        {
            using var evidence=JsonDocument.Parse(File.ReadAllText("docs/research/break2d-sdk.json"));var root=evidence.RootElement;
            var digest=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(root.GetProperty("assembly").GetString()!)));
            if(digest!=root.GetProperty("sha256").GetString())throw new Exception("Installed custom class changed; getter review is stale.");
            foreach(string name in new[]{"get_BreakType","get_BreakTypeIndex","get_GapBetweenLines","get_VectSkew","get_TargetDrawingViewId"})
            {
                var method=root.GetProperty("methods").EnumerateArray().Single(m=>m.GetProperty("method").GetString()==name);
                var bytes=Convert.FromHexString(method.GetProperty("il").GetString()!);
                if(bytes.Length!=7 || bytes[0]!=0x02 || bytes[1]!=0x7B || bytes[6]!=0x2A || method.GetProperty("direct_calls").GetArrayLength()!=0 || method.GetProperty("indirect_calli_count").GetInt32()!=0)
                    throw new Exception("Getter is not reviewed ldarg.0/ldfld/ret: "+name);
            }
            return Task.CompletedTask;
        });
        await test("drawing break catalog distinguishes unavailable details and opaque native parameters",()=>
        {
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.get_drawing_views");var output=tool.OutputSchema!.Value.GetRawText();
            foreach(var field in new[]{"attached_break_ids","breaks_unavailable_reason","adapter_schema","class_id","gap_between_lines","skew_vector","target_view_id","parameter_space"})
                if(!output.Contains('"'+field+'"'))throw new Exception("Missing read contract: "+field);
            if(ToolCatalog.Tools.Count!=ToolCatalog.OperationNames.Length || !tool.Description!.Contains("breaks:null") || !tool.Description.Contains("native_break_parameters") || !tool.Description.Contains("No dynamic-property discovery"))throw new Exception("Unknown custom-class boundary omitted.");
            if(tool.InputSchema.GetRawText().Contains("gap_between_lines"))throw new Exception("Read capability exposes a mutation field.");
            return Task.CompletedTask;
        });
    }
}

