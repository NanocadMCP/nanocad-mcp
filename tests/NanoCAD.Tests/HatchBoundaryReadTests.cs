using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class HatchBoundaryReadTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("hatch boundary reader exposes association and functional creation with recovery notice",()=>{
            var create=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.create_entities").InputSchema;
            if(!create.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray().Any(v=>v.GetProperty("properties").TryGetProperty("boundary_handle",out _)))throw new Exception("Functional writer absent.");
            var hatch=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_objects").OutputSchema!.Value.GetProperty("$defs").GetProperty("hatch");
            var refs=hatch.GetProperty("properties").GetProperty("loops").GetProperty("items").GetProperty("properties").GetProperty("associated_handles");
            if(refs.GetProperty("type").GetString()!="array")throw new Exception("Stored reference schema/version missing.");
            return Task.CompletedTask;
        });
        await test("hatch boundary reference write normalizes identity without a rollback gate",()=>{
            var entities=CreateValidation.ParseEntities(Wire.Element(new[]{new{type="Hatch",boundary_handle="00ab"}}),allowDimensions:true);
            if(entities[0] is not AssociativeHatchSpec h||h.BoundaryHandle!="AB")throw new Exception("Boundary handle not normalized.");
            return Task.CompletedTask;
        });
    }
}



