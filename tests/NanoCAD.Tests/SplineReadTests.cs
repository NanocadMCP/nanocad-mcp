using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class SplineReadTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        SplineGeometry Valid()=>new(1,false,false,false,true,false,new[]{new[]{0d,0d,0d},new[]{3d,4d,0d}},new[]{0d,0d,1d,1d},Array.Empty<double>(),1e-7,1e-7,0,1,new[]{0d,0d,0d},new[]{3d,4d,0d});
        void Deny(Action action) { try{action();}catch(DomainException){return;}throw new Exception("Invalid spline accepted"); }
        await test("spline verification preserves NURBS representation and rejects inconsistent arrays domain endpoints",()=>{
            var s=Valid();SplineVerification.Verify(s);
            SplineVerification.Verify(s with{weights=new[]{1d,1d}});
            SplineVerification.Verify(s with{rational=true,weights=new[]{1d,.5}});
            foreach(var bad in new[]{s with{degree=6},s with{degree=2},s with{has_fit_data=true},s with{periodic=true},s with{knots=new[]{0d,0d,1d}},s with{knots=new[]{0d,-1d,1d,1d}},s with{end_parameter=2},s with{start_point=new[]{1d,0d,0d}},s with{control_points=new[]{new[]{0d,0d},new[]{3d,4d,0d}}},s with{rational=true},s with{weights=new[]{1d,2d}},s with{rational=true,weights=new[]{1d,0d}},s with{rational=true,weights=new[]{1d,double.NaN}},s with{closed=true},s with{knot_tolerance=-1},s with{end_parameter=double.PositiveInfinity}})
                Deny(()=>SplineVerification.Verify(bad));
            SplineVerification.CheckScope(3,129,false,false);return Task.CompletedTask;
        });
        await test("clamped closed spline scope retains flags and bounded XY pairs with exact degree1 distance",()=>{
            var closed=new SplineGeometry(1,false,true,true,true,false,new[]{new[]{0d,0d,0d},new[]{3d,0d,0d},new[]{3d,4d,0d},new[]{0d,0d,0d}},new[]{0d,0d,1d,2d,3d,3d},Array.Empty<double>(),1e-7,1e-7,0,3,new[]{0d,0d,0d},new[]{0d,0d,0d});
            SplineVerification.Verify(closed);SplineVerification.Verify(closed with{periodic=false});
            Deny(()=>SplineVerification.Verify(closed with{knots=new[]{-1d,0d,1d,2d,3d,4d}}));
            Deny(()=>SplineVerification.Verify(closed with{end_point=new[]{0d,0.001,0d}}));
            SplineVerification.RequirePairScope(closed);CurvedEditScope.Require(closed);
            SplineVerification.RequirePairScope(closed,"intersections");SplineVerification.RequirePairScope(closed,"relations");
            SplineVerification.RequirePairScope(closed,"distance");
            var raised=closed with{control_points=closed.control_points.Select(p=>new[]{p[0],p[1],1e-12}).ToArray()};
            Deny(()=>SplineVerification.RequirePairScope(raised,"intersections"));
            Deny(()=>SplineVerification.RequirePairScope(raised,"relations"));
            SplineVerification.RequirePairScope(closed with{control_points=Enumerable.Repeat(closed.control_points[0],65).ToArray()},"intersections");
            var controls=closed.control_points.Select(p=>p.ToArray()).ToArray();controls[^1][0]=1e-8;
            Deny(()=>CurvedEditScope.Require(closed with{control_points=controls}));
            var spec=new SplineSpec(1,closed.control_points.Select(p=>new Point2(p[0],p[1])).ToArray(),closed.knots,Array.Empty<double>(),"0");
            Deny(()=>SplineCreation.RequireMatch(spec,closed));
            return Task.CompletedTask;
        });
        await test("spline metrics validates positive finite native parameter distance",()=>{
            if(SplineVerification.LengthFromDistances(3,8)!=5)throw new Exception("Parameter origin lost");
            foreach(var p in new[]{new[]{0d,0d},new[]{3d,2d},new[]{-1d,3d},new[]{0d,double.NaN},new[]{0d,double.PositiveInfinity}})
                Deny(()=>SplineVerification.LengthFromDistances(p[0],p[1]));return Task.CompletedTask;
        });
        await test("spline paired guard bounds control count and requires exact XY without projection",()=>{
            var s=Valid();SplineVerification.RequirePairScope(s);
            SplineVerification.RequirePairScope(s with{control_points=Enumerable.Repeat(new[]{0d,0d,0d},64).ToArray()});
            SplineVerification.RequirePairScope(s with{control_points=Enumerable.Repeat(new[]{0d,0d,0d},65).ToArray()});
            if(!SplineVerification.LiesInWcsXy(s)||SplineVerification.LiesInWcsXy(s with{control_points=new[]{new[]{0d,0d,1e-12},new[]{3d,4d,0d}}}))throw new Exception("Spline XY guard projected an off-plane control");
            return Task.CompletedTask;
        });
        await test("spline read filter schema and capability retain bounded detail and paired scope",()=>{
            var input=Wire.Element(new{document_id=Guid.NewGuid().ToString("D"),types=new[]{"Spline"}});
            if(Validation.Parse("query_objects",input).Types!.Single()!="Spline")throw new Exception("Missing filter");
            var query=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.query_objects");var inspect=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_objects");
            if(!query.InputSchema.GetProperty("properties").GetProperty("types").GetProperty("items").GetProperty("enum").EnumerateArray().Any(i=>i.GetString()=="Spline"))throw new Exception("Missing schema filter");
            var geometry=inspect.OutputSchema!.Value.GetProperty("$defs").GetProperty("spline");
            foreach(var field in Wire.Element(Valid()).EnumerateObject())if(!geometry.GetProperty("properties").TryGetProperty(field.Name,out _))throw new Exception("DTO field missing");
            if(geometry.GetProperty("properties").GetProperty("control_points").TryGetProperty("maxItems",out _))throw new Exception("No collection bound");
            if(ToolCatalog.Tools.Count!=ToolCatalog.OperationNames.Length||ToolCatalog.Tools.Single(t=>t.Name=="nanocad.analyze_geometry").Description!.Contains("at most64 controls"))throw new Exception("Scope leaked");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));return Task.CompletedTask;
        });
    }
}
