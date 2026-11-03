using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingConstraints431Tests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        var id=Guid.NewGuid().ToString("D");
        JsonObject Input(params JsonObject[] specs)=>new(){["bridge_instance_id"]=id,["document_id"]=id,["expected_revision"]="1",["operation_id"]=id,["units"]="Millimeters",
            ["operations"]=new JsonArray(new JsonObject{["operation"]="add_drawing_constraints",["handles"]=new JsonArray("A","B","C"),["constraints"]=new JsonArray(specs.Select(s=>s.DeepClone()).ToArray())})};
        void Deny(Action call){try{call();}catch(DomainException){return;}throw new Exception("Ambiguous drawing relation accepted.");}
        await test("drawing block431 symmetry binds an actual distinct third source and fix has no point",()=>{
            var symmetric=new JsonObject{["kind"]="symmetric",["first"]="A",["second"]="B",["third"]="C"};
            var fixedObject=new JsonObject{["kind"]="fix",["first"]="C"};
            var edit=(DrawingProfileEdit)ModifyValidation.Parse(Wire.Element(Input(symmetric,fixedObject))).Operations.Single();
            if(edit.Constraints![0].Third!="C"||edit.Constraints[1].FirstPoint!=null)throw new Exception("Third operand or whole-object selection lost.");
            symmetric["third"]="A";Deny(()=>ModifyValidation.Parse(Wire.Element(Input(symmetric))));symmetric["third"]="D";
            Deny(()=>ModifyValidation.Parse(Wire.Element(Input(symmetric))));symmetric["third"]="C";
            fixedObject["first_point"]="start";Deny(()=>ModifyValidation.Parse(Wire.Element(Input(fixedObject))));
            symmetric["first_point"]="start";Deny(()=>ModifyValidation.Parse(Wire.Element(Input(symmetric))));return Task.CompletedTask;
        });
        await test("drawing block431 symmetry checks finite reflected geometry in its current frame",()=>{
            var axis=new SketchConstraintCurve("C","line",new(0,-5),new(0,5));
            var left=new SketchConstraintCurve("A","circle",Center:new(-8,2),Radius:3);
            var right=new SketchConstraintCurve("B","circle",Center:new(8,2),Radius:3);
            var spec=new SketchConstraintSpec("symmetric","A","B",null,null,null,"C");
            DrawingProfileGeometry.Validate(new[]{spec,new SketchConstraintSpec("fix","C",null,null,null,null)},new[]{axis,left,right});
            Deny(()=>DrawingProfileGeometry.Validate(new[]{spec},new[]{axis,left,right with{Radius=4}}));
            Deny(()=>DrawingProfileGeometry.Validate(new[]{spec},new[]{axis,left,right with{Center=new(8,3)}}));
            var a=new SketchConstraintCurve("A","line",new(-6,0),new(-2,4));var b=new SketchConstraintCurve("B","line",new(2,4),new(6,0));
            DrawingProfileGeometry.Validate(new[]{spec},new[]{axis,a,b});
            Deny(()=>DrawingProfileGeometry.Validate(new[]{spec},new[]{axis,a,b with{End=new(6,1)}}));return Task.CompletedTask;
        });
        await test("drawing block431 schemas publish whole-object fix and symmetry separately from fix-point",async()=>{
            var result=await new ToolService().CallAsync("get_guidance",new Dictionary<string,System.Text.Json.JsonElement>{
                ["topic"]=Wire.Element("schema"),["operation"]=Wire.Element("modify_entities"),["variant"]=Wire.Element("add_drawing_constraints")},CancellationToken.None);
            using var focused=System.Text.Json.JsonDocument.Parse(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text);
            var operation=focused.RootElement.GetProperty("properties").GetProperty("operations").GetProperty("items");
            string description=operation.GetProperty("description").GetString()!;
            var choices=operation.GetProperty("properties").GetProperty("constraints").GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
            var symmetric=choices.Single(branch=>branch.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Any(value=>value.GetString()=="symmetric"));
            var fixedObject=choices.Single(branch=>branch.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Any(value=>value.GetString()=="fix"));
            if(!symmetric.GetProperty("properties").TryGetProperty("third",out _)||fixedObject.GetProperty("properties").TryGetProperty("first_point",out _)||
                !description.Contains("separate Line handle in third")||!description.Contains("anchors one complete"))throw new Exception("Focused drawing selection semantics not published.");
            var relation=new DrawingRelationChange("relation","symmetric","A","B",null,null,"C");
            if(Wire.Element(relation).GetProperty("third_handle").GetString()!="C")throw new Exception("Axis not returned in drawing relation receipt.");
        });
        await test("drawing block431 retained Polyline operands preserve exact straight and clockwise arc roles",()=>{
            var shape=new PolylineShape(false,new[]{new PolylineShapeVertex(new(0,0),0,0,0),new(new(10,0),-1,0,0),new(new(10,10),0,0,0)});
            var polyline=new SketchConstraintCurve("A","polyline",Polyline:shape);
            var line=new SketchConstraintCurve("B","line",new(10,0),new(15,0));
            DrawingProfileGeometry.Validate(new[]{new SketchConstraintSpec("horizontal","A",null,null,null,null,FirstSegmentIndex:0),
                new("coincident","A","B","start","start",null,FirstSegmentIndex:1),
                new("radius","A",null,null,null,5,FirstSegmentIndex:1),new("fix","A",null,null,null,null)},new[]{polyline,line});
            var arc=DrawingConstraintOperands431.Resolve(polyline,1);
            if(arc.Kind!="arc"||arc.Start!=new Point2(10,10)||arc.End!=new Point2(10,0)||!DrawingProfileGeometry.Same(DrawingConstraintOperands431.Point(polyline,1,"start"),new(10,0)))throw new Exception("Clockwise native endpoint role was reversed.");
            Deny(()=>DrawingProfileGeometry.Validate(new[]{new SketchConstraintSpec("horizontal","B",null,null,null,null,FirstSegmentIndex:0)},new[]{polyline,line}));
            Deny(()=>DrawingProfileGeometry.Validate(new[]{new SketchConstraintSpec("horizontal","A",null,null,null,null)},new[]{polyline,line}));
            Deny(()=>DrawingConstraintOperands431.Resolve(polyline,2));return Task.CompletedTask;
        });
        await test("drawing block431 contract allows distinct same-parent segment relations without fabricated handles",()=>{
            var relation=new JsonObject{["kind"]="perpendicular",["first"]="A",["second"]="A",["first_segment_index"]=0,["second_segment_index"]=1};
            var edit=(DrawingProfileEdit)ModifyValidation.Parse(Wire.Element(Input(relation))).Operations.Single();
            if(edit.Constraints![0].First!="A"||edit.Constraints[0].FirstSegmentIndex!=0||edit.Constraints[0].SecondSegmentIndex!=1)throw new Exception("Actual parent/segment selections lost.");
            relation["second_segment_index"]=0;Deny(()=>ModifyValidation.Parse(Wire.Element(Input(relation))));relation["second_segment_index"]=-1;
            Deny(()=>ModifyValidation.Parse(Wire.Element(Input(relation))));return Task.CompletedTask;
        });
        await test("drawing block431 native shared-vertex receipts preserve the actual segment driver identity",()=>{
            var shape=new PolylineShape(false,[new(new(60,5),0,0,0),new(new(70,5),0,0,0),new(new(70,15),0,0,0)]);
            var curve=new SketchConstraintCurve("53D","polyline",Polyline:shape);
            var sources=new Dictionary<string,SketchConstraintCurve>{{curve.Id,curve}};
            // Captured native431 receipt: its first pick is vertex1, represented
            // as end(edge0), though the request selected start(edge1).
            var observed=new SketchConstraintSpec("distance","53D","53D","end","end",10,FirstSegmentIndex:0,SecondSegmentIndex:1);
            var normalized=DrawingConstraintOperands431.Canonical(observed,sources);
            if(normalized.Kind!="length"||normalized.FirstSegmentIndex!=1||normalized.Second!=null||normalized.FirstPoint!=null||normalized.SecondPoint!=null)
                throw new Exception("Shared native vertex was mistaken for a different segment dimension.");
            if(!DrawingConstraintOperands431.SameReference("53D",0,"end","53D",1,"start",sources))throw new Exception("Identical native shared vertex was rejected.");
            if(DrawingConstraintOperands431.SameReference("53D",0,"start","53D",1,"end",sources))throw new Exception("Different native vertices were conflated.");
            var span=observed with{FirstPoint="start"};
            if(DrawingConstraintOperands431.Canonical(span,sources).Kind!="distance")throw new Exception("Across-corner distance was incorrectly converted to one segment length.");
            return Task.CompletedTask;
        });
    }
}
