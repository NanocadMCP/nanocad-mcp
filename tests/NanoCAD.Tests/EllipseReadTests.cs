using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class EllipseReadTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        EllipseGeometry Valid()=>new(new[]{0d,0d,0d},new[]{10d,0d,0d},new[]{0d,5d,0d},10,5,.5,new[]{0d,0d,1d},new[]{10d,0d,0d},new[]{10d,0d,0d},0,2*Math.PI,true);
        await test("ellipse verification accepts finite WCS axes and rejects inconsistent native geometry",()=>{
            var e=Valid();EllipseVerification.Verify(e);
            EllipseVerification.Verify(e with{end_parameter=Math.PI/2,end_point=new[]{0d,5d,0d},closed=false});
            EllipseVerification.Verify(e with{minor_axis=new[]{0d,0d,5d},normal=new[]{0d,-1d,0d}});
            foreach(var bad in new[]{e with{major_radius=0},e with{radius_ratio=2},e with{minor_radius=4},e with{start_point=new[]{11d,0d,0d}},e with{end_parameter=0},e with{closed=false},e with{normal=new[]{0d,0d,-1d}},e with{center=new[]{double.NaN,0d,0d}},e with{minor_axis=new[]{3d,4d,0d}},e with{major_axis=new[]{10d,0d}}})
            {bool denied=false;try{EllipseVerification.Verify(bad);}catch(DomainException){denied=true;}if(!denied)throw new Exception("Invalid native ellipse accepted.");}return Task.CompletedTask;
        });
        await test("ellipse metrics validates positive finite native interval distance",()=>{
            if(EllipseVerification.LengthFromDistances(3,12)!=9)throw new Exception("Native interval origin lost.");
            foreach(var pair in new[]{new[]{0d,0d},new[]{3d,2d},new[]{-1d,2d},new[]{0d,double.NaN},new[]{double.NegativeInfinity,1d}})
            {bool denied=false;try{EllipseVerification.LengthFromDistances(pair[0],pair[1]);}catch(DomainException){denied=true;}if(!denied)throw new Exception("Invalid native length accepted.");}return Task.CompletedTask;
        });
        await test("ellipse read filters and schema preserve WCS details",()=>{
            var input=Wire.Element(new{document_id=Guid.NewGuid().ToString("D"),types=new[]{"Ellipse"}});if(Validation.Parse("query_objects",input).Types!.Single()!="Ellipse")throw new Exception("Ellipse filter unavailable.");
            var query=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.query_objects");var inspect=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_objects");
            if(!query.InputSchema.GetProperty("properties").GetProperty("types").GetProperty("items").GetProperty("enum").EnumerateArray().Any(i=>i.GetString()=="Ellipse"))throw new Exception("Ellipse filter absent from schema.");
            var geometry=inspect.OutputSchema!.Value.GetProperty("$defs").GetProperty("ellipse");
            foreach(string key in new[]{"center","major_axis","minor_axis","major_radius","minor_radius","radius_ratio","normal","start_point","end_point","start_parameter","end_parameter","closed"})
                if(!geometry.GetProperty("properties").TryGetProperty(key,out _))throw new Exception("Ellipse DTO field missing.");
            var creation=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.create_entities").InputSchema;
            if(ToolCatalog.Tools.Count!=ToolCatalog.OperationNames.Length||inspect.Annotations?.ReadOnlyHint!=true)throw new Exception("Read extension changed tools.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));return Task.CompletedTask;
        });
    }
}
