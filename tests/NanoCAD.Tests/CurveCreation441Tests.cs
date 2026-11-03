using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class CurveCreation441Tests
{
    static void Need(bool condition,string message){if(!condition)throw new Exception(message);}
    static CreateArguments Parse(object[] entities,string space="model")=>CreateValidation.Parse(Wire.Element(new{
        bridge_instance_id="11111111-1111-1111-1111-111111111111",document_id="22222222-2222-2222-2222-222222222222",
        expected_revision="1",operation_id="33333333-3333-3333-3333-333333333333",units="Millimeters",target_space=space,entities}));
    static void Deny(Action action){try{action();}catch(DomainException){return;}throw new Exception("Invalid coordinate/representation request admitted.");}
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("block441 spatial and closed fit grammars preserve coordinates and existing planar creation",()=>
        {
            var result=Parse([
                new{type="Spline",construction="fit_points_3d",fit_points=new[]{new[]{0d,0,0},new[]{5d,7,9},new[]{12d,4,20}},fit_tolerance=0.01},
                new{type="Polyline3d",vertices=new[]{new[]{0d,0,0},new[]{4d,8,12}}},
                new{type="Spline",construction="closed_fit_points",fit_points=new[]{new[]{0d,0},new[]{10d,0},new[]{8d,6},new[]{0d,5}},fit_tolerance=0.01},
                new{type="Spline",construction="fit_points",fit_points=new[]{new[]{0d,0},new[]{5d,7},new[]{12d,4}},fit_tolerance=0.01}]);
            Need(result.Entities[0] is SpatialFitSplineSpec441 spatial&&spatial.FitPoints[1].Z==9,"XYZ was lost or projected.");
            Need(result.Entities[1] is Polyline3dSpec441 poly&&!poly.Closed&&poly.Vertices[1].Z==12,"Native3D vertices changed.");
            Need(result.Entities[2] is PeriodicFitSplineSpec441 closed&&closed.FitPoints.Length==4,"Closed fit input confused with controls.");
            Need(result.Entities[3] is FitSplineSpec408 old&&old.FitPoints[1].Y==7,"Existing planar branch changed.");
            var spatialInput=new{type="Spline",construction="fit_points_3d",fit_points=new[]{new[]{0d,0,0},new[]{4d,8,12}},fit_tolerance=0.01};
            Deny(()=>Parse([spatialInput],"paper"));
            Deny(()=>Parse([new{type="Spline",construction="fit_points_3d",fit_points=new[]{new[]{0d,0,0},new[]{4d,8,12}},fit_tolerance=0.01,start_tangent=new[]{1d,0,0}}]));
            Deny(()=>Parse([new{type="Spline",construction="closed_fit_points",fit_points=new[]{new[]{0d,0},new[]{4d,0},new[]{0d,3},new[]{0d,0}},fit_tolerance=0.01}]));
            return Task.CompletedTask;
        });
        await test("block441 published creation exposes spatial and closed fit inputs without fixed point caps",()=>
        {
            var input=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.create_entities").InputSchema;
            var branches=input.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
            foreach(string name in new[]{"fit_points_3d","closed_fit_points"})
            {
                var branch=branches.Single(b=>b.GetProperty("properties").TryGetProperty("construction",out var c)&&c.TryGetProperty("const",out var k)&&k.GetString()==name);
                var points=branch.GetProperty("properties").GetProperty("fit_points");
                Need(!points.TryGetProperty("maxItems",out _),"Artificial point-count cap.");
                Need(points.GetProperty("items").GetProperty("minItems").GetInt32()==(name=="fit_points_3d"?3:2),"Point frame/dimension hidden.");
            }
            Need(branches.Any(b=>b.GetProperty("properties").GetProperty("type").GetProperty("const").GetString()=="Polyline3d"),"3D polyline missing from publication.");
            var query=Validation.Parse("query_objects",Wire.Element(new{document_id="22222222-2222-2222-2222-222222222222",types=new[]{"Polyline3d"}}));
            Need(query.Types!.Single()=="Polyline3d","Published3D polyline query is not admitted.");
            return Task.CompletedTask;
        });
    }
}
