using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

internal static class NativeSketchCurves445Tests
{
    static void Need(bool condition,string message){if(!condition)throw new Exception(message);}
    static NativeSketchInitialization385 Parse(params object[] geometry)=>NativeSketchValidation385.Parse(Wire.Element(geometry),Wire.Element(Array.Empty<object>()),DrawingWorkplane.XY,"native_geometry");
    static JsonObject Spline()=>JsonNode.Parse("""{"key":"upper","kind":"spline","points":[[0,0,0],[10,0,0],[10,10,0],[0,10,0]],"degree":3,"knots":[0,0,0,0,1,1,1,1]}""")!.AsObject();
    static JsonObject Ellipse()=>JsonNode.Parse("""{"key":"oval","kind":"ellipse","points":[[40,0,0],[60,0,0],[40,10,0]]}""")!.AsObject();
    static void Reject(Action action){try{action();}catch(DomainException){return;}throw new Exception("Invalid native sketch curve reached mutation admission.");}
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("block445 native sketch NURBS and Ellipse preserve defining data and old geometry",()=>
        {
            var result=Parse(Spline(),Ellipse(),new{key="line",kind="line",points=new[]{new[]{0d,0,0},new[]{0d,10,0}}});
            Need(result.Geometry[0].Spline is {Degree:3,Closed:false} s&&s.Knots.Length==8&&s.Weights.Length==0,"Native spline control definition lost.");
            Need(result.Geometry[0].Points.Length==4&&result.Geometry[0].Points[1]==new Point3(10,0,0),"Controls became samples or local coordinates.");
            Need(result.Geometry[1].Ellipse is {StartAngle:0} e&&e.EndAngle==2*Math.PI&&result.Geometry[1].Points[2]==new Point3(40,10,0),"Ellipse defining axes or whole closure lost.");
            Need(result.Geometry[2].Kind=="line"&&result.Geometry[2].Spline==null&&result.Geometry[2].Ellipse==null,"Old native line route changed.");
            var restored=Wire.Element(result).Deserialize<NativeSketchInitialization385>(Wire.Json)!;
            Need(restored.Geometry[0].Spline!.Knots.SequenceEqual(result.Geometry[0].Spline!.Knots),"Typed curve contract round trip lost NURBS data.");return Task.CompletedTask;
        });
        await test("block445 native sketch curve validation rejects noncoplanar and incomplete representations before writes",()=>
        {
            var wrongPlane=Spline();wrongPlane["points"]![1]![2]=1;Reject(()=>Parse(wrongPlane));
            var badKnots=Spline();badKnots["knots"]![4]=0;Reject(()=>Parse(badKnots));
            var missing=Spline();missing.Remove("degree");Reject(()=>Parse(missing));
            var wrongDegree=Spline();wrongDegree["degree"]="3";Reject(()=>Parse(wrongDegree));
            var weights=Spline();weights["weights"]=new JsonArray(1,0,1,1);Reject(()=>Parse(weights));
            var falselyClosed=Spline();falselyClosed["closed"]=true;Reject(()=>Parse(falselyClosed));
            var axis=Ellipse();axis["points"]![2]![0]=41;Reject(()=>Parse(axis));
            var crossPlane=Ellipse();crossPlane["points"]![2]![2]=5;Reject(()=>Parse(crossPlane));
            var turns=Ellipse();turns["end_angle"]=7;Reject(()=>Parse(turns));
            var unknown=Ellipse();unknown["degree"]=3;Reject(()=>Parse(unknown));return Task.CompletedTask;
        });
        await test("block445 closed rational native NURBS retain explicit definition",()=>
        {
            var row=Spline();row["points"]![3]=new JsonArray(0,0,0);row["closed"]=true;row["weights"]=new JsonArray(1,0.5,0.5,1);
            var curve=Parse(row).Geometry.Single();Need(curve.Spline is {Closed:true} s&&s.Weights.SequenceEqual(new[]{1d,.5,.5,1}),"Closed rational native input normalized to independent line segments.");return Task.CompletedTask;
        });
        await test("block445 first native model creation accepts complete curved sketches through the public grammar",()=>
        {
            ModelExtrusionSpec First(object[] geometry,double[] interior)
            {
                var request=Wire.Element(new{bridge_instance_id="11111111-1111-1111-1111-111111111111",document_id="22222222-2222-2222-2222-222222222222",expected_revision="1",operation_id="33333333-3333-3333-3333-333333333333",units="Millimeters",
                    entities=new[]{new{type="Solid3d",kind="model_extrusion",plane=new{origin=new[]{0d,0,0},x_axis=new[]{1d,0,0},y_axis=new[]{0d,1,0}},native_geometry=geometry,region_point=interior,distance=5d,direction="positive"}}});
                return (ModelExtrusionSpec)CreateValidation.Parse(request).Entities.Single();
            }
            var spline=First([Spline(),new{key="closure",kind="line",points=new[]{new[]{0d,10,0},new[]{0d,0,0}}}],[3,5,0]);
            Need(spline.NativeGeometry?.Geometry[0].Spline?.Degree==3&&spline.RegionPoint==new Point3(3,5,0),"First native curved sketch was forced through the legacy polygon profile grammar.");
            var ellipse=First([Ellipse()],[40,0,0]);Need(ellipse.NativeGeometry?.Geometry.Single().Ellipse!=null,"First native Ellipse profile omitted from the real creation grammar.");return Task.CompletedTask;
        });
    }
}
