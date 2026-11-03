using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
internal static class TextFormat442Tests
{
    static void Need(bool yes,string why){if(!yes)throw new Exception(why);}
    static void Reject(Action action){try{action();}catch(DomainException){return;}throw new Exception("Invalid input accepted.");}
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        JsonObject Input()=>new(){["operation"]="set_text_format",["handles"]=new JsonArray("A1"),["text_token"]=new string('B',64),
            ["properties"]=new JsonObject{["horizontal_mode"]="center",["vertical_mode"]="middle",["text_height"]=4.5,["anchor_wcs"]=new JsonArray(12,8,0)}};
        await test("block442 TEXT typography keeps explicit native anchors and batches distinct notes",()=>
        {
            var a=TextFormatValidation442.Parse(Wire.Element(Input()),"$",["A1"]);
            Need(a.Properties.AnchorWcs==new Point3(12,8,0)&&a.Properties.HorizontalMode=="center"&&a.Properties.TextHeight==4.5,"Explicit anchor/typography changed.");
            var b=TextFormatValidation442.Parse(Wire.Element(Input()),"$",["A2"]);TextFormatValidation442.Batch([a,b]);
            Reject(()=>TextFormatValidation442.Batch([a,a]));Reject(()=>TextFormatValidation442.Batch([a,new EraseSpec(["A2"])]));
            var bad=Input();bad["properties"]!["horizontal_mode"]="guess";Reject(()=>TextFormatValidation442.Parse(Wire.Element(bad),"$",["A1"]));
            bad=Input();bad["properties"]!["oblique"]=Math.PI/2;Reject(()=>TextFormatValidation442.Parse(Wire.Element(bad),"$",["A1"]));return Task.CompletedTask;
        });
        await test("block442 TEXT creation distinguishes fixed anchor and native two-anchor fitting",()=>
        {
            var center=TextFormatValidation442.Create(Wire.Element(new{horizontal_mode="center",vertical_mode="top",rotation=.2}),"$");
            Need(center.HorizontalMode=="center"&&center.VerticalMode=="top"&&center.SecondAnchor==null,"Single anchor typography changed.");
            var fit=TextFormatValidation442.Create(Wire.Element(new{horizontal_mode="fit",second_anchor=new[]{20,0}}),"$");
            Need(fit.SecondAnchor==new Point2(20,0),"Explicit fitted endpoint lost.");
            Reject(()=>TextFormatValidation442.Create(Wire.Element(new{horizontal_mode="fit"}),"$"));
            Reject(()=>TextFormatValidation442.Create(Wire.Element(new{horizontal_mode="fit",second_anchor=new[]{20,0},width_factor=2}),"$"));
            Reject(()=>TextFormatValidation442.Create(Wire.Element(new{horizontal_mode="fit",second_anchor=new[]{20,0},rotation=.3}),"$"));
            var old=new TextSpec("label",new(0,0),3,"Standard","0");
            Need(!JsonSerializer.Serialize(old,Wire.Json).Contains("Format442",StringComparison.OrdinalIgnoreCase),"Legacy creation fingerprint gained an omitted format field.");return Task.CompletedTask;
        });
    }
}
