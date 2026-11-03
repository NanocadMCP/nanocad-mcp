using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class PointCloudReadTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        var bridge=Guid.NewGuid().ToString("D");var doc=Guid.NewGuid().ToString("D");
        await test("point cloud metadata requires exact context and one canonical handle",()=>
        {
            var input=new{bridge_instance_id=bridge,document_id=doc,expected_revision="18446744073709551615",handle="000aF"};
            var args=PointCloudReadValidation.Parse(Wire.Element(input));
            if(args.Handles!.Single()!="AF" || args.ExpectedRevision!="18446744073709551615")throw new Exception("Identity precision lost.");
            void Reject(object value)
            {
                try{PointCloudReadValidation.Parse(Wire.Element(value));}
                catch(DomainException){return;}
                throw new Exception("Invalid cloud input accepted.");
            }
            Reject(new{document_id=doc,expected_revision="1",handle="AF"});
            Reject(new{bridge_instance_id=bridge,document_id=doc,handle="AF"});
            Reject(new{bridge_instance_id=bridge,document_id=doc,expected_revision="1",handle="0"});
            Reject(new{bridge_instance_id=bridge,document_id=doc,expected_revision="1",handle="AF",load=true});
            using var duplicate=JsonDocument.Parse(JsonSerializer.Serialize(input)[..^1]+",\"handle\":\"B0\"}");
            try{PointCloudReadValidation.Parse(duplicate.RootElement);throw new Exception("Duplicate identity accepted.");}
            catch(DomainException){}
            return Task.CompletedTask;
        });
        await test("point cloud prototype remains quarantined after unverified native point count",()=>
        {
            if(Wire.Operations.Contains("inspect_point_cloud") || ToolCatalog.Tools.Any(t=>t.Name=="nanocad.inspect_point_cloud"))throw new Exception("Unverified cloud adapter leaked into catalog.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
            return Task.CompletedTask;
        });
    }
}
