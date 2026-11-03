using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class SplineConstraint445Tests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("Smooth445 native selectors and unique endpoint picks are independent observations",()=>{
            var actual=new Dictionary<string,Point3>{{"start",new(20,20,0)},{"end",new(0,0,0)}};
            string? Role(string? subtype,Point3? point,bool spline=true,bool subentity=true)=>NativeCurvePointBinding445.Resolve(subentity,subtype,spline,point,actual);
            if(Role("Start",null)!="start"||Role("End",new(double.NaN,double.NaN,double.NaN))!="end")
                throw new Exception("A decoded native selector wrongly required its optional pick.");
            if(Role("Point",new(20,20,0))!="start"||Role("Curve",new(0,0,0))!="end")
                throw new Exception("A generic Spline selector lost its uniquely observed actual WCS endpoint.");
            if(Role("Point",null)!=null||Role("Point",new(20,0,20))!=null||Role("Mid",new(20,20,0))!=null||Role("Point",new(20,20,0),spline:false)!=null)
                throw new Exception("An unsupported selector/frame or missing point was guessed.");
            actual["end"]=actual["start"];if(Role("Point",new(20,20,0))!=null)throw new Exception("An ambiguous closed seam was assigned an endpoint role.");
            return Task.CompletedTask;
        });
        CurveSplineGeometry445 Shape()=>new(3,false,false,false,true,false,
            [[0,0,0],[3,0,0],[7,2,0],[10,2,0]],[0,0,0,0,1,1,1,1],[],1e-10,1e-10,0,1,[0,0,0],[10,2,0],
            null,null,"complete_reported_native_nurbs",new(3,4,false,false));
        SketchConstraintCurve Spline()=>new("A","spline",new(0,0),new(10,2),Spline:Shape(),StartTangent:new(9,0),EndTangent:new(9,0));
        void Deny(Action action){try{action();}catch(DomainException){return;}throw new Exception("Invalid Smooth geometry was accepted.");}
        await test("Smooth445 native Spline-first order preserves complete public endpoint bindings",()=>{
            var spline=Spline();var line=new SketchConstraintCurve("B","line",new(-5,0),new(0,0));
            var curves=new Dictionary<string,SketchConstraintCurve>{{"A",spline},{"B",line}};
            var request=new SketchConstraintSpec("smooth","B","A","end","start",null);
            var native=SplineConstraintGeometry445.NativeOrder(request,line.Kind,spline.Kind);
            DrawingProfileGeometry.Validate([request,native],curves.Values.ToArray());
            if(native.First!="A"||native.Second!="B"||native.FirstPoint!="start"||native.SecondPoint!="end"||request.First!="B"||request.FirstPoint!="end")
                throw new Exception("Native Smooth reordered identities without their actual endpoint roles or mutated the public request.");
            if(DrawingProfileGeometry.Point(curves[native.First],native.FirstPoint)!=DrawingProfileGeometry.Point(curves[request.Second!],request.SecondPoint)||
                DrawingProfileGeometry.Point(curves[native.Second!],native.SecondPoint)!=DrawingProfileGeometry.Point(curves[request.First],request.FirstPoint))
                throw new Exception("The normalized native operands no longer describe the requested physical endpoints.");
            var polyline=new SketchConstraintCurve("C","polyline",Polyline:new(false,[new(new(-5,0),0,0,0),new(new(-2,0),0,0,0),new(new(0,0),0,0,0)]));
            var segmentRequest=request with{First="C",FirstSegmentIndex=1};
            var segmentNative=SplineConstraintGeometry445.NativeOrder(segmentRequest,"line","spline");
            DrawingProfileGeometry.Validate([segmentRequest,segmentNative],[spline,polyline]);
            if(segmentNative.FirstSegmentIndex!=null||segmentNative.SecondSegmentIndex!=1||segmentNative.SecondPoint!="end"||
                DrawingConstraintOperands431.Point(polyline,segmentNative.SecondSegmentIndex,segmentNative.SecondPoint)!=new Point2(0,0))
                throw new Exception("A Polyline segment/vertex binding was detached from its operand during Smooth normalization.");
            var distance=request with{Kind="distance",Value=0};
            if(SplineConstraintGeometry445.NativeOrder(native,"spline","line")!=native||
                SplineConstraintGeometry445.NativeOrder(request,"spline","spline")!=request||
                SplineConstraintGeometry445.NativeOrder(distance,"line","spline")!=distance)
                throw new Exception("Native Smooth ordering changed an already valid order or an unrelated directional constraint.");
            return Task.CompletedTask;
        });
        await test("Smooth445 requires real spline endpoint continuity without a G2 claim",()=>{
            var spline=Spline();var line=new SketchConstraintCurve("B","line",new(-5,0),new(0,0));
            var spec=new SketchConstraintSpec("smooth","A","B","start","end",null);
            DrawingProfileGeometry.Validate([spec],[spline,line]);
            Deny(()=>DrawingProfileGeometry.Validate([spec],[spline,line with{End=new(0,.1)}]));
            Deny(()=>DrawingProfileGeometry.Validate([spec],[spline with{StartTangent=new(9,1)},line]));
            Deny(()=>DrawingProfileGeometry.Validate([spec],[spline with{StartTangent=new(0,0)},line]));
            Deny(()=>DrawingProfileGeometry.Validate([spec],[spline,line with{Start=new(5,0)}]));
            Deny(()=>DrawingProfileGeometry.Validate([spec],[spline with{Spline=Shape() with{closed=true}},line]));
            Deny(()=>DrawingProfileGeometry.Validate([spec with{FirstPoint="center"}],[spline,line]));
            Deny(()=>DrawingProfileGeometry.Validate([spec],[line with{Id="A"},line]));
            return Task.CompletedTask;
        });
        await test("Smooth445 retains full spline and fit definition beyond endpoints",()=>{
            var before=Spline();var controls=before.Spline!.control_points.Select(p=>p.ToArray()).ToArray();controls[1][1]=.1;
            if(!DrawingProfileGeometry.Same(before,Spline())||DrawingProfileGeometry.Same(before,before with{Spline=before.Spline with{control_points=controls}}))
                throw new Exception("Interior Spline definition change was missed.");
            if(DrawingProfileGeometry.Same(before,before with{Spline=before.Spline with{knots=[0,0,0,0,2,2,2,2],end_parameter=2}}))
                throw new Exception("Native knot/domain change was missed.");
            var fit=new CurveSplineFit445(3,[[0,0,0],[10,2,0]],1e-6,true,[1,0,0],[1,0,0],"Chord");
            var fitted=before.Spline with{has_fit_data=true,fit_data=fit};
            SplineConstraintGeometry445.Complete(fitted);
            if(SplineConstraintGeometry445.SameDefinition(fitted,fitted with{fit_data=fit with{fit_tolerance=1e-5}}))
                throw new Exception("Fitted Spline tolerance change was missed.");
            Deny(()=>SplineConstraintGeometry445.Complete(fitted with{fit_data=null}));
            return Task.CompletedTask;
        });
        await test("Smooth445 permits retained Spline point controls and native Polyline endpoints",()=>{
            var spline=Spline();var line=new SketchConstraintCurve("B","line",new(-5,0),new(0,0));
            DrawingProfileGeometry.Validate([new("fix_point","A",null,"end",null,null),new("distance","A","B","end","end",Math.Sqrt(104))],[spline,line]);
            var polyline=new SketchConstraintCurve("C","polyline",Polyline:new(false,[new(new(-5,0),0,0,0),new(new(0,0),0,0,0)]));
            DrawingProfileGeometry.Validate([new("smooth","A","C","start","end",null,SecondSegmentIndex:0)],[spline,polyline]);
            return Task.CompletedTask;
        });
        await test("Smooth445 ellipse roles retain real center and exclude a closed seam",()=>{
            var full=new SketchConstraintCurve("E","ellipse",new(10,0),new(10,0),new(0,0),
                StartTangent:new(0,5),EndTangent:new(0,5),Ellipse:new(new(0,0,0),new(1,0,0),new(0,1,0),10,5,0,2*Math.PI));
            DrawingProfileGeometry.Validate([new("fix","E",null,null,null,null),new("fix_point","E",null,"center",null,null)],[full]);
            Deny(()=>DrawingProfileGeometry.Validate([new("fix_point","E",null,"start",null,null)],[full]));
            var partial=full with{Start=new(0,-5),Ellipse=full.Ellipse! with{StartAngle=-Math.PI/2,EndAngle=0}};
            if(DrawingProfileGeometry.Same(full,partial)||DrawingProfileGeometry.Same(full,full with{Ellipse=full.Ellipse! with{MinorRadius=6}}))
                throw new Exception("Changed native ellipse definition was ignored.");
            if(DrawingProfileGeometry.Point(partial,"end")!=new Point2(10,0))throw new Exception("An actual open elliptical-arc endpoint was lost.");
            return Task.CompletedTask;
        });
        await test("Smooth445 parser and actual focused schema agree on point-bound relation",async()=>{
            string id=Guid.NewGuid().ToString("D");
            var relation=new JsonObject{["kind"]="smooth",["first"]="A",["second"]="B",["first_point"]="start",["second_point"]="end"};
            var op=new JsonObject{["operation"]="add_drawing_constraints",["handles"]=new JsonArray("A","B"),["constraints"]=new JsonArray(relation)};
            var input=new JsonObject{["bridge_instance_id"]=id,["document_id"]=id,["expected_revision"]="1",["operation_id"]=id,["units"]="Millimeters",["operations"]=new JsonArray(op)};
            var edit=(DrawingProfileEdit)ModifyValidation.Parse(Wire.Element(input)).Operations.Single();
            if(edit.Constraints!.Single().Kind!="smooth")throw new Exception("Smooth contract disappeared.");
            relation.Remove("second_point");Deny(()=>ModifyValidation.Parse(Wire.Element(input)));relation["second_point"]="end";
            relation["first_point"]="center";Deny(()=>ModifyValidation.Parse(Wire.Element(input)));relation["first_point"]="start";
            relation["value"]=1;Deny(()=>ModifyValidation.Parse(Wire.Element(input)));relation.Remove("value");
            var result=await new ToolService().CallAsync("get_guidance",new Dictionary<string,System.Text.Json.JsonElement>{["topic"]=Wire.Element("schema"),["operation"]=Wire.Element("modify_entities"),["variant"]=Wire.Element("add_drawing_constraints")},CancellationToken.None);
            using var focused=System.Text.Json.JsonDocument.Parse(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text);
            var operation=focused.RootElement.GetProperty("properties").GetProperty("operations").GetProperty("items");
            var smooth=operation.GetProperty("properties").GetProperty("constraints").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(branch=>branch.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Any(v=>v.GetString()=="smooth"));
            if(!smooth.GetProperty("required").EnumerateArray().Any(v=>v.GetString()=="second_point")||!operation.GetProperty("description").GetString()!.Contains("no G2 continuity"))
                throw new Exception("Published Smooth contract lacks truthful endpoint/continuity semantics.");
        });
    }
}
