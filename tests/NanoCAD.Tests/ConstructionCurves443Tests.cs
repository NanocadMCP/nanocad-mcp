using System.Text.Json.Nodes;
using NanoCAD.Contracts;

internal static class ConstructionCurves443Tests
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(Action action){try{action();}catch(DomainException){return;}throw new Exception("Invalid construction curve accepted.");}
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("block443 Ray Xline retain base direction and normalize extreme finite vectors",()=>
        {
            foreach(string type in new[]{"Ray","Xline"})
            {
                var value=ConstructionCurves443.Parse(Wire.Element(new{type,base_point=new[]{12.0,-7,3},direction=new[]{-1e308,1e308,0}}),"$","0");
                Need(value.Type==type&&value.BasePoint==new Point3(12,-7,3),"Native type/base changed.");
                Need(double.IsFinite(value.UnitDirection.X)&&Math.Abs(value.UnitDirection.X+Math.Sqrt(.5))<1e-14&&value.UnitDirection.Y>0,"Normalization overflowed or reversed the ray.");
            }
            Reject(()=>ConstructionCurves443.Direction(new(0,0,0)));Reject(()=>ConstructionCurves443.Direction(new(double.PositiveInfinity,0,0)));
            return Task.CompletedTask;
        });
        await test("block443 construction curves expose explicit native inputs and reject invented extent fields",()=>
        {
            var value=new JsonObject{["type"]="Ray",["base_point"]=new JsonArray(0,0,0),["direction"]=new JsonArray(1,0,0)};
            ConstructionCurves443.Parse(Wire.Element(value),"$","0");value["length"]=100;Reject(()=>ConstructionCurves443.Parse(Wire.Element(value),"$","0"));
            value.Remove("length");value["base_point"]=new JsonArray(1,2);Reject(()=>ConstructionCurves443.Parse(Wire.Element(value),"$","0"));
            return Task.CompletedTask;
        });
    }
}
