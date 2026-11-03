using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelProfileRebind430Tests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
        static void Reject(Action action){try{action();}catch(DomainException){return;}throw new Exception("Invalid replacement selection was accepted.");}
        const string feature="{00000000-0000-0000-0000-000000000001}",old="{00000000-0000-0000-0000-000000000002}",next="{00000000-0000-0000-0000-000000000003}",sketch="{00000000-0000-0000-0000-000000000004}";
        JsonObject Input()=>new(){["operation"]="set_model_extrusion_profile",["handles"]=new JsonArray("A1"),["feature_id"]=feature,
            ["expected_current_profile_id"]=old,["expected_last_item_id"]=feature,["expected_sketch_id"]=sketch,["expected_profile_token"]=new string('A',64)};
        ModelProfileRebindSpec349 Parse(JsonObject input)=>(ModelProfileRebindSpec349)ModelProfileRebindValidation349.Parse(Wire.Element(input),"$",["A1"]);
        await test("block430 replacement profile input distinguishes exact retained ID and current-sketch selection",()=>
        {
            var input=Input();input["profile_id"]=next;var existing=Parse(input);
            Need(existing.ProfileId==next&&existing.ReplacementRegions==null,"Existing shared-profile route changed.");
            input.Remove("profile_id");input["replacement_regions"]=JsonSerializer.SerializeToNode(new[]{new[]{6.0,0.0,0.0},new[]{30.0,0.0,0.0}});
            var selected=Parse(input);Need(selected.ProfileId==old&&selected.ExpectedSketchId==sketch&&selected.ReplacementRegions?.Length==2,"Current-profile preflight identity or WCS seeds were lost.");
            input["profile_id"]=next;Reject(()=>Parse(input));input.Remove("profile_id");
            input["replacement_regions"]=new JsonArray();Reject(()=>Parse(input));
            input["replacement_regions"]=JsonSerializer.SerializeToNode(new[]{new[]{6.0,0.0,0.0},new[]{6.0,0.0,0.0}});Reject(()=>Parse(input));
            input.Remove("replacement_regions");Reject(()=>Parse(input));input["profile_id"]=old;Reject(()=>Parse(input));
            var schema=ModelProfileRebindSchema349.Operation();Need(schema["oneOf"] is JsonArray{Count:2},"Published route exclusivity is missing.");
            Need(!schema["required"]!.AsArray().Any(value=>value!.GetValue<string>()=="profile_id"),"Profile ID must not be required for the selection route.");
            var tool=ToolCatalog.Tools.Single(value=>value.Name=="nanocad.modify_entities");
            Need(tool.InputSchema.GetRawText().Contains("replacement_regions",StringComparison.Ordinal)&&tool.OutputSchema!.Value.GetRawText().Contains("profile_selection_created",StringComparison.Ordinal),"Public tool did not publish selection input/receipt.");
            return Task.CompletedTask;
        });
        await test("block430 native replacement region plan preserves annular holes and independent regions",()=>
        {
            SketchConstraintCurve Circle(string id,double x,double radius)=>new(id,"circle",Center:new(x,0),Radius:radius);
            var curves=new[]{Circle("outer",0,10),Circle("bore",0,4),Circle("island",30,3)};
            var regions=ModelProfileRebindRegions430.Select(curves,[new(6,0),new(30,0)],()=>{});
            Need(regions.Length==2,"Separate desired regions were collapsed.");
            var ring=regions.Single(region=>region.Outer.Single().Id=="outer");
            Need(ring.Holes.Length==1&&ring.Holes[0].Single().Id=="bore","Annular hole was substituted with the outer loop.");
            Reject(()=>ModelProfileRebindRegions430.Select(curves,[new(6,0),new(7,0)],()=>{}));
            Reject(()=>ModelProfileRebindRegions430.Select(curves,[new(6,0),new(0,0)],()=>{}));
            Reject(()=>ModelProfileRebindRegions430.Select(curves,[new(10,0)],()=>{}));
            Reject(()=>ModelProfileRebindRegions430.Select(curves,[new(100,0)],()=>{}));
            var plane=new SketchPlaneSpec(new(5,10,20),new(1,0,0),new(0,0,1));
            var local=DrawingWorkplane.Local(plane,new(11,10,20));Need(local==new Point2(6,0),"WCS seed lost the oriented sketch plane.");
            Reject(()=>DrawingWorkplane.Local(plane,new(11,11,20)));
            bool cancelled=false;try{ModelProfileRebindRegions430.Select(curves,[new(6,0)],()=>throw new OperationCanceledException());}catch(OperationCanceledException){cancelled=true;}
            Need(cancelled,"Selection classification ignored cancellation.");return Task.CompletedTask;
        });
        SketchConstraintCurve Circle440(string id,double x,double y,double radius)=>new(id,"circle",Center:new(x,y),Radius:radius);
        SketchConstraintCurve[] Box440(string prefix,double x,double y,double halfX,double halfY)
        {
            Point2[] vertices=[new(x-halfX,y-halfY),new(x+halfX,y-halfY),new(x+halfX,y+halfY),new(x-halfX,y+halfY)];
            return vertices.Select((point,index)=>new SketchConstraintCurve(prefix+index,"line",point,vertices[(index+1)%4])).ToArray();
        }
        await test("block440 replacement mixed loops retain holes actual IDs and independent nested cells",()=>
        {
            var box=Box440("plate",0,0,10,6);var bore=Circle440("bore",0,0,4);var island=Circle440("island",0,0,2);
            var separate=Box440("boss",30,0,3,3);var curves=box.Concat(new[]{bore,island}).Concat(separate).ToArray();
            var selected=ModelProfileRebindRegions430.Select(curves,[new(8,0),new(0,0),new(30,0)],()=>{});
            Need(selected.Length==3,"Mixed-loop cells were merged or dropped.");var plate=selected.Single(region=>region.Outer.Length==4&&region.Outer.Any(curve=>curve.Id=="plate0"));
            Need(plate.Outer.Select(curve=>curve.Id).ToHashSet().SetEquals(box.Select(curve=>curve.Id))&&plate.Holes.Single().Single()==bore,
                "Actual plate member identities or circular hole were replaced.");
            Need(selected.Single(region=>region.Outer.Length==1&&region.Outer[0].Id=="island").Holes.Length==0,"A nonadjacent nested island gained an incorrect hole.");
            var circleOuter=Circle440("circleOuter",0,0,20);var rectangularHole=Box440("inner",0,0,4,3);
            var inverse=ModelProfileRebindRegions430.Select(new[]{circleOuter}.Concat(rectangularHole).ToArray(),[new(12,0)],()=>{}).Single();
            Need(inverse.Outer.Single()==circleOuter&&inverse.Holes.Single().Select(curve=>curve.Id).ToHashSet().SetEquals(rectangularHole.Select(curve=>curve.Id)),
                "Line holes inside a Circle were lost.");return Task.CompletedTask;
        });
        await test("block440 replacement arc loops preserve circular holes and reject unresolved topology before CAD",()=>
        {
            SketchConstraintCurve[] slot=[new("bottom","line",new(-5,-3),new(5,-3)),new("right","arc",new(5,-3),new(5,3),new(5,0),3,Math.PI),
                new("top","line",new(5,3),new(-5,3)),new("left","arc",new(-5,3),new(-5,-3),new(-5,0),3,Math.PI)];
            var hole=Circle440("slotBore",0,0,1);var curves=slot.Append(hole).ToArray();
            var selected=ModelProfileRebindRegions430.Select(curves,[new(3,0)],()=>{}).Single();
            Need(ModelProfileBoundary340.Same(slot,selected.Outer)&&selected.Holes.Single().Single()==hole,"Arc boundary or hole was approximated.");
            Reject(()=>ModelProfileRebindRegions430.Select(curves,[new(3,0),new(4,0)],()=>{}));
            Reject(()=>ModelProfileRebindRegions430.Select(curves,[new(3,0),new(0,0)],()=>{}));
            Reject(()=>ModelProfileRebindRegions430.Select(curves,[new(1,0)],()=>{}));
            Reject(()=>ModelProfileRebindRegions430.Select(curves,[new(30,0)],()=>{}));
            Reject(()=>ModelProfileRebindRegions430.Select(slot.Append(Circle440("touch",0,0,3)).ToArray(),[new(6,0)],()=>{}));
            Reject(()=>ModelProfileRebindRegions430.Select(slot.Append(Circle440("cross",0,0,4)).ToArray(),[new(6,0)],()=>{}));
            Reject(()=>ModelProfileRebindRegions430.Select(Box440("a",0,0,4,3).Concat(Box440("b",8,0,4,3)).ToArray(),[new(0,0)],()=>{}));
            Reject(()=>ModelProfileRebindRegions430.Select(Box440("a",0,0,4,3).Concat(Box440("b",2,0,4,3)).ToArray(),[new(-3,0)],()=>{}));
            Reject(()=>ModelProfileRebindRegions430.Select(curves.Append(new("open","line",new(20,0),new(25,0))).ToArray(),[new(3,0)],()=>{}));
            bool cancelled=false;try{ModelProfileRebindRegions430.Select(curves,[new(3,0)],()=>throw new OperationCanceledException());}catch(OperationCanceledException){cancelled=true;}
            Need(cancelled,"Mixed-loop topology ignored cancellation.");return Task.CompletedTask;
        });
    }
}
