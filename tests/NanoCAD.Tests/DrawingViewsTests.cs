using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingViewsTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        Dictionary<string,object> Input()=>new(){["bridge_instance_id"]=Guid.NewGuid().ToString("D"),["document_id"]=Guid.NewGuid().ToString("D"),["expected_revision"]="12",["layout_handle"]="000aB"};
        await test("drawing views require explicit current layout context and reject write controls",()=>
        {
            var args=Validation.Parse("get_drawing_views",Wire.Element(Input()));
            if(args.Handles![0]!="AB")throw new Exception("Layout handle not normalized.");
            var selected=Input();selected["geometry_view_handle"]="000cd";
            if(Validation.Parse("get_drawing_views",Wire.Element(selected)).GeometryViewHandle!="CD")
                throw new Exception("Selected projection view handle not normalized.");
            selected["geometry_view_handle"]="invalid";
            try{Validation.Parse("get_drawing_views",Wire.Element(selected));throw new Exception("Invalid projection handle accepted.");}
            catch(DomainException){}
            foreach(string name in new[]{"bridge_instance_id","document_id","expected_revision","layout_handle"})
            {
                var value=Input();value.Remove(name);
                try{Validation.Parse("get_drawing_views",Wire.Element(value));}catch(DomainException){continue;}throw new Exception("Missing context accepted: "+name);
            }
            foreach(string name in new[]{"handle","regenerate","save","apply"})
            {
                var value=Input();value[name]=true;
                try{Validation.Parse("get_drawing_views",Wire.Element(value));}catch(DomainException){continue;}throw new Exception("Unsupported control accepted: "+name);
            }
            return Task.CompletedTask;
        });
        await test("drawing views catalog exposes stored read-only metadata and excludes quarantined capabilities",()=>
        {
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.get_drawing_views");
            if(tool.Annotations is not{ReadOnlyHint:true,DestructiveHint:false})throw new Exception("Unsafe annotations.");
            if(!tool.Description!.Contains("geometry_view_handle") || !tool.Description.Contains("No regeneration") ||
               !tool.Description.Contains("dimensions do not automatically follow"))throw new Exception("Selected projection scope omitted.");
            if(ToolCatalog.Tools.Count!=ToolCatalog.OperationNames.Length+1||!ToolCatalog.Tools.Any(t=>t.Name is "nanocad.inspect_sketch_constraints"))throw new Exception("Wrong capability catalog.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
            return Task.CompletedTask;
        });
    }
}
