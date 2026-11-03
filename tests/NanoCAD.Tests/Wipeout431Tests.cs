using System.Text.Json.Nodes;
using NanoCAD.Contracts;

internal static class Wipeout431Tests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        static void Reject(Action action){try{action();}catch(DomainException){return;}throw new Exception("Ambiguous native mask was accepted.");}
        JsonObject Request()=>new(){["type"]="Wipeout",["boundary"]=new JsonArray(new JsonArray(0,0),new JsonArray(20,0),new JsonArray(20,10),new JsonArray(0,10))};
        await test("block431 native mask polygon excludes self crossings duplicate closure and fake frame flags",()=>
        {
            var accepted=WipeoutValidation431.ParseCreate(Wire.Element(Request()),"$","0");if(accepted.Type!="Wipeout"||accepted.Boundary.Length!=4)throw new Exception("Native mask shape lost.");
            var crossed=Request();crossed["boundary"]=new JsonArray(new JsonArray(0,0),new JsonArray(20,10),new JsonArray(20,0),new JsonArray(0,10));Reject(()=>WipeoutValidation431.ParseCreate(Wire.Element(crossed),"$","0"));
            var closure=Request();closure["boundary"]!.AsArray().Add(new JsonArray(0,0));Reject(()=>WipeoutValidation431.ParseCreate(Wire.Element(closure),"$","0"));
            var frame=Request();frame["has_frame"]=false;Reject(()=>WipeoutValidation431.ParseCreate(Wire.Element(frame),"$","0"));return Task.CompletedTask;
        });
        await test("block431 native mask boundary editing requires current explicit geometry selection",()=>
        {
            var request=new JsonObject{["operation"]="set_wipeout_boundary",["handles"]=new JsonArray("A1"),["expected_geometry_token"]=new string('A',64),["boundary_world"]=new JsonArray(new JsonArray(0,0,5),new JsonArray(10,0,5),new JsonArray(10,8,5))};
            var accepted=WipeoutValidation431.ParseEdit(Wire.Element(request),"$",["A1"]);if(accepted.BoundaryWorld[0].Z!=5)throw new Exception("WCS mask plane lost.");
            Reject(()=>WipeoutValidation431.Batch([accepted,new MoveSpec(["A1"],new(1,0))]));
            request.Remove("expected_geometry_token");Reject(()=>WipeoutValidation431.ParseEdit(Wire.Element(request),"$",["A1"]));return Task.CompletedTask;
        });
    }
}
