using System.Text.Json.Nodes;
using NanoCAD.Contracts;

internal static class NativeSmooth445Tests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        JsonObject Relation(string first="a",string second="b",string a="end",string b="start")=>new()
        {["kind"]="smooth",["first"]=first,["second"]=second,["first_point"]=a,["second_point"]=b};
        void Reject(JsonArray rows)
        {
            try{SketchConstraintValidation385.Parse(Wire.Element(rows),"constraints",false);}
            catch(DomainException e)when(e.Error.code=="INVALID_ARGUMENT"){return;}
            throw new Exception("Invalid or duplicate Smooth endpoint binding was admitted.");
        }
        await test("native smooth445 binds explicit endpoints and rejects reversed duplicate or center",()=>
        {
            var parsed=SketchConstraintValidation385.Parse(Wire.Element(new JsonArray(Relation())),"constraints",false).Single();
            if(parsed.FirstPoint!="end"||parsed.SecondPoint!="start"||parsed.Value!=null)throw new Exception("Native Smooth lost its endpoint roles.");
            Reject(new JsonArray(Relation(),Relation("b","a","start","end")));
            Reject(new JsonArray(Relation(a:"center")));
            var missing=Relation();missing.Remove("second_point");Reject(new JsonArray(missing));
            var literal=Relation();literal["value"]=2;Reject(new JsonArray(literal));
            return Task.CompletedTask;
        });
    }
}
