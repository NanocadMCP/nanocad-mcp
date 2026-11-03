using System.Text.Json.Nodes;
using NanoCAD.Contracts;

internal static class DrawingGroups431Tests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        static void Need(bool ok){if(!ok)throw new Exception("Native group selection changed.");}
        static void Reject(Action action){try{action();}catch(DomainException){return;}throw new Exception("An ambiguous group lifecycle request was accepted.");}
        JsonObject Existing(string action)=>new(){["operation"]=action,["handle"]="A1",["expected_name"]="Mounting geometry",["expected_group_token"]=new string('B',64)};
        await test("block431 native groups preserve explicit member selection and reject ambiguous membership",()=>
        {
            var request=new JsonObject{["operation"]="create_group",["name"]="Mounting geometry",["handles"]=new JsonArray("a2","A3")};
            var created=DrawingGroups431.Parse(Wire.Element(request),"$");Need(created.AddHandles.SequenceEqual(["A2","A3"])&&created.Handle==null);
            var change=Existing("set_group");change["add_handles"]=new JsonArray("A4");change["remove_handles"]=new JsonArray("A2");
            var updated=DrawingGroups431.Parse(Wire.Element(change),"$");Need(updated.Handle=="A1"&&updated.AddHandles.SequenceEqual(["A4"])&&updated.RemoveHandles.SequenceEqual(["A2"]));
            change["add_handles"]=new JsonArray("A2");Reject(()=>DrawingGroups431.Parse(Wire.Element(change),"$"));
            request["handles"]=new JsonArray("A2","a2");Reject(()=>DrawingGroups431.Parse(Wire.Element(request),"$"));
            return Task.CompletedTask;
        });
        await test("block431 dissolve removes only a selected current group with no member erase fields",()=>
        {
            var request=Existing("dissolve_group");var edit=DrawingGroups431.Parse(Wire.Element(request),"$");Need(edit.AddHandles.Length==0&&edit.RemoveHandles.Length==0);
            request["handles"]=new JsonArray("A2");Reject(()=>DrawingGroups431.Parse(Wire.Element(request),"$"));request.Remove("handles");
            request["expected_group_token"]="stale";Reject(()=>DrawingGroups431.Parse(Wire.Element(request),"$"));
            Reject(()=>DrawingGroups431.Batch([edit,new CreateLayerEdit("Other",new(null,null,null,null,null,null,null))]));
            return Task.CompletedTask;
        });
    }
}
