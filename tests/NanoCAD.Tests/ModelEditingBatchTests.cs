using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelEditingBatchTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        string id = Guid.NewGuid().ToString("D");
        ModifyArguments Parse(params object[] operations) => ModifyValidation.Parse(Wire.Element(new
        { bridge_instance_id=id, document_id=id, operation_id=id, expected_revision="7", units="Millimeters", operations }));
        object Solid(object transform, string[]? handles=null) => new { operation="transform_solid", handles=handles??new[]{"00AF"}, transform };
        void Reject(params object[] operations)
        {
            try { Parse(operations); } catch (DomainException) { return; }
            throw new Exception("Invalid edit accepted.");
        }
        await test("model editing batch supports bounded XYZ transforms without changing legacy XY", () =>
        {
            var transform = (TransformSolidSpec)Parse(Solid(new{kind="rotation",axis_origin=new[]{1d,2,3},axis_direction=new[]{0d,1,0},angle=Math.PI/2})).Operations.Single();
            if(transform.Handles.Single()!="AF" || transform.Transform.Origin!=new Point3(1,2,3) || transform.Transform.Direction!=new Point3(0,1,0))
                throw new Exception("XYZ identity/axis was lost.");
            Parse(Solid(new{kind="translation",displacement=new[]{0d,0,25}}),Solid(new{kind="mirror",plane_origin=new[]{0d,0,0},plane_normal=new[]{1d,0,0}}),Solid(new{kind="scale",center=new[]{0d,0,0},factor=2}));
            if(Parse(new{operation="move",handles=new[]{"A"},displacement=new[]{2d,3}}).Operations.Single() is not MoveSpec{Displacement: {X:2,Y:3}})
                throw new Exception("Legacy move changed.");
            Reject(new{operation="move",handles=new[]{"A"},displacement=new[]{2d,3,4}});
            Reject(Solid(new{kind="rotation",axis_origin=new[]{0d,0,0},axis_direction=new[]{0d,0,0},angle=1}));
            Reject(Solid(new{kind="scale",center=new[]{0d,0,0},factor=-1}));
            Reject(Solid(new{kind="translation",displacement=new[]{1e6,1e6,0}}));
            Reject(Solid(new{kind="translation",displacement=new[]{1d,2}}));
            Reject(Solid(new{kind="scale",center=new[]{0d,0,0},factor=2,strip_history=true}));
            Reject(Solid(new{kind="scale",center=new[]{0d,0,0},factor=2},Enumerable.Range(1,9).Select(n=>n.ToString("X")).ToArray()));
            Reject(Enumerable.Range(0,33).Select(_=>Solid(new{kind="translation",displacement=new[]{1d,0,0}})).ToArray());
            return Task.CompletedTask;
        });
        await test("model editing batch vertex expectations are bounded and canonical", () =>
        {
            object Vertex(int index, double x) => new{index,expected_position=new[]{x,0d},position=new[]{x,10d}};
            object Edit(params object[] vertices) => new{operation="set_polyline_vertices",handles=new[]{"A"},vertices};
            var forward=Parse(Edit(Vertex(0,0),Vertex(2,20)));
            var reverse=Parse(Edit(Vertex(2,20),Vertex(0,0)));
            if(forward.Fingerprint()!=reverse.Fingerprint())throw new Exception("Independent vertex order changed request identity.");
            if(((PolylineVerticesSpec)forward.Operations.Single()).Vertices[1].ExpectedPosition!=new Point2(20,0))throw new Exception("Expectation lost.");
            Reject(Edit(Vertex(1,0),Vertex(1,1))); Reject(Edit(Vertex(-1,0))); Reject(Edit(Vertex(1000,0)));
            Reject(Edit()); Reject(Edit(Enumerable.Range(0,129).Select(n=>Vertex(n,n)).ToArray()));
            Reject(new{operation="set_polyline_vertices",handles=new[]{"A","B"},vertices=new[]{Vertex(0,0)}});
            Reject(new{operation="erase",handles=new[]{"A"}},Edit(Vertex(0,0)));
            Reject(Edit(new{index=1,position=new[]{0d,10}}));
            return Task.CompletedTask;
        });
        await test("model editing batch curve replacement retains explicit expected geometry", () =>
        {
            object Circle(double radius) => new{type="Circle",center=new[]{0d,0},radius};
            object Edit(object expected, object geometry) => new{operation="set_curve_geometry",handles=new[]{"A"},expected_geometry=expected,geometry};
            var first=Parse(Edit(Circle(10),Circle(15)));
            if(first.Operations.Single() is not CurveGeometrySpec{ExpectedGeometry: {Radius:10},Geometry: {Radius:15}})
                throw new Exception("Circle geometry lost.");
            if(first.Fingerprint()==Parse(Edit(Circle(11),Circle(15))).Fingerprint() || first.Fingerprint()==Parse(Edit(Circle(10),Circle(16))).Fingerprint())
                throw new Exception("Request identity omits expectation or replacement geometry.");
            Parse(Edit(new{type="Line",start=new[]{0d,0},end=new[]{10d,0}},new{type="Line",start=new[]{1d,2},end=new[]{20d,3}}));
            Reject(Edit(Circle(10),Circle(0)));
            Reject(Edit(Circle(10),new{type="Line",start=new[]{0d,0},end=new[]{10d,0}}));
            Reject(Edit(new{type="Line",start=new[]{0d,0},end=new[]{10d,0}},new{type="Line",start=new[]{1d,1},end=new[]{1d,1}}));
            Reject(new{operation="set_curve_geometry",handles=new[]{"A"},geometry=Circle(15)});
            return Task.CompletedTask;
        });
        await test("model editing batch schemas disclose direct body scope and vertex identity", () =>
        {
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            var operations=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
            var solid=operations.Single(op=>op.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="transform_solid");
            var vertices=operations.Single(op=>op.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="set_polyline_vertices");
            if(solid.GetProperty("properties").GetProperty("handles").GetProperty("maxItems").GetInt32()!=8 || !solid.GetProperty("description").GetString()!.Contains("MultiCAD"))
                throw new Exception("Unbounded or misleading direct solid scope.");
            if(vertices.GetProperty("properties").GetProperty("handles").GetProperty("maxItems").GetInt32()!=1 || !vertices.GetProperty("description").GetString()!.Contains("expected_position"))
                throw new Exception("Vertex identity contract missing.");
            return Task.CompletedTask;
        });
    }
}
