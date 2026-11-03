using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingAngularTests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        SketchConstraintCurve L(string id,double x,double y,double u,double v)=>new(id,"line",new(x,y),new(u,v));
        void Near(double a,double b){if(Math.Abs(a-b)>1e-8)throw new Exception($"Angular geometry mismatch: {a} vs {b}");}
        void Deny(Action action){try{action();}catch(DomainException){return;}throw new Exception("Invalid angular geometry accepted");}
        await test("drawing angle preserves acute obtuse and clockwise sectors rather than acute supplements",()=>{
            var first=L("A",0,0,20,0);
            foreach(double theta in new[]{Math.PI/6,2*Math.PI/3,-2*Math.PI/3})
            {
                var second=L("B",0,0,15*Math.Cos(theta),15*Math.Sin(theta));var s=DrawingAngularGeometry.Measure(first,second);
                Near(s.Radians,Math.Abs(theta));Near(Math.Atan2(s.Annotation.Y,s.Annotation.X),theta/2);
                var reverse=L("C",second.End!.X,second.End.Y,0,0);Near(DrawingAngularGeometry.Measure(first,reverse).Radians,Math.PI-Math.Abs(theta));
                DrawingAngularGeometry.Validate(new("angle","A","B",null,null,Math.Abs(theta)*180/Math.PI),first,second);
                Deny(()=>DrawingAngularGeometry.Validate(new("angle","A","B",null,null,(Math.PI-Math.Abs(theta))*180/Math.PI),first,second));
            }
            return Task.CompletedTask;
        });
        await test("drawing angle resolves offset line intersections and rejects singular bounds and types",()=>{
            var first=L("A",10,20,30,20);var second=L("B",15,10,15,40);var s=DrawingAngularGeometry.Measure(first,second);
            Near(s.Intersection.X,15);Near(s.Intersection.Y,20);Near(s.Radians,Math.PI/2);
            Deny(()=>DrawingAngularGeometry.Measure(first,L("B",0,1,20,1)));
            Deny(()=>DrawingAngularGeometry.Measure(first,L("B",20,1,0,1)));
            Deny(()=>DrawingAngularGeometry.Measure(first,L("B",0,0,0,0)));
            Deny(()=>DrawingAngularGeometry.Measure(first,new("B","circle",Center:new(0,0),Radius:5)));
            Deny(()=>DrawingAngularGeometry.Measure(first,L("B",2e6,0,2e6,20)));
            return Task.CompletedTask;
        });
        await test("drawing angle contract uses degrees and distinct sources with complete schema",()=>{
            var id=Guid.NewGuid().ToString();object Input(object relation)=>new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="1",units="Millimeters",operations=new[]{new{operation="add_drawing_constraints",handles=new[]{"A","B"},constraints=new[]{relation}}}};
            var parsed=ModifyValidation.Parse(Wire.Element(Input(new{kind="angle",first="A",second="B",value=60})));if(((DrawingProfileEdit)parsed.Operations.Single()).Constraints![0].Kind!="angle")throw new Exception("Angle absent");
            Deny(()=>ModifyValidation.Parse(Wire.Element(Input(new{kind="angle",first="A",second="B",value=180}))));
            Deny(()=>ModifyValidation.Parse(Wire.Element(Input(new{kind="angle",first="A",second="A",value=1}))));
            var schema=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema.GetRawText();if(!schema.Contains("degrees")||!schema.Contains("\"angle\""))throw new Exception("Angular schema absent");return Task.CompletedTask;
        });
    }
}
