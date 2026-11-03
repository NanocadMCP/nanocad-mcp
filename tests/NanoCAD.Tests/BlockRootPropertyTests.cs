using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

internal static class BlockRootPropertyTests
{
    private static JsonElement Before()=>Wire.Element(new{
        state=new{position=new[]{100,200,0},Rotation=.3,properties=new{
            layer="A1",linetype="B1",color=5,lineweight=35,definition="C1",scale=new[]{2,2,2},
            stored=new{common=new{visible=true,color=new{method="true_color",aci=(int?)null,rgb=new[]{10,120,230}},linetype="DASH",lineweight=new{mode="explicit",value_hundredth_mm=(int?)35,native_value=35}},
                LinetypeScale=2.5,material="D1",plot_type="ByLayer",plot_id=(string?)null,transparency=new{alpha=178,by_alpha=true,by_layer=false,by_block=false}}},
            attributes=new[]{new{Position=new{X=107,Y=219},Rotation=.4,Properties=new{Height=4,text="Actual",style="E1",color="ByBlock"}}}},
        layer_name="Original",identity=new{root="F1",owner="F2",attributes=new[]{new{id="F3",owner="F1"}}},alignments=new[]{new[]{96,204,0}}});
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Check(bool value){if(!value)throw new Exception("Block root property expectation failed.");}
        var empty=new BlockPropertySymbols(null,null,null,null);
        await test("block root properties replace RGB deliberately and keep exact native ACI modes",()=>{
            var before=Before();
            foreach(int aci in new[]{0,1,7,255,256})
            {
                var after=BlockRootProperties.Expected(before,new(null,aci,null,null),empty);
                var color=after.GetProperty("state").GetProperty("properties").GetProperty("stored").GetProperty("common").GetProperty("color");
                Check(color.GetProperty("method").GetString()==(aci==0?"by_block":aci==256?"by_layer":"aci"));
                Check(color.GetProperty("rgb").ValueKind==JsonValueKind.Null);
                Check(aci is 0 or 256?color.GetProperty("aci").ValueKind==JsonValueKind.Null:color.GetProperty("aci").GetInt32()==aci);
                Check(after.GetProperty("state").GetProperty("properties").GetProperty("color").GetInt32()==aci);
                Check(BlockMoveGeometry.SameProperties(before.GetProperty("state").GetProperty("attributes"),after.GetProperty("state").GetProperty("attributes")));
            }
            Check(Before().GetRawText()==before.GetRawText());return Task.CompletedTask;
        });
        await test("block root sparse property expectations preserve source geometry attributes owners and transparency",()=>{
            var before=Before();
            var first=BlockRootProperties.Expected(before,new("target",null,null,null),new("Target","A2",null,null));
            var expected=BlockRootProperties.Expected(first,new(null,null,"bylayer",-2),new(null,null,"ByLayer","B2"));
            var p=expected.GetProperty("state").GetProperty("properties");
            Check(expected.GetProperty("layer_name").GetString()=="Target"&&p.GetProperty("layer").GetString()=="A2"&&p.GetProperty("linetype").GetString()=="B2");
            Check(p.GetProperty("stored").GetProperty("common").GetProperty("lineweight").GetProperty("mode").GetString()=="by_block");
            foreach(var field in new[]{"identity","alignments"})Check(BlockMoveGeometry.SameProperties(before.GetProperty(field),expected.GetProperty(field)));
            Check(BlockMoveGeometry.SameProperties(before.GetProperty("state").GetProperty("attributes"),expected.GetProperty("state").GetProperty("attributes")));
            foreach(var field in new[]{"LinetypeScale","material","plot_type","plot_id","transparency"})Check(BlockMoveGeometry.SameProperties(before.GetProperty("state").GetProperty("properties").GetProperty("stored").GetProperty(field),p.GetProperty("stored").GetProperty(field)));
            foreach(var field in new[]{"position","attributes","properties"})
            {
                var changed=JsonNode.Parse(expected.GetRawText())!;
                if(field=="position")changed["state"]![field]![0]=101;
                else if(field=="attributes")changed["state"]![field]![0]!["Properties"]!["style"]="WRONG";
                else changed["state"]![field]!["stored"]!["transparency"]!["alpha"]=255;
                Check(!BlockMoveGeometry.SameProperties(expected,Wire.Element(changed)));
            }
            return Task.CompletedTask;
        });
        await test("block root property expectations require resolved symbol identities before mutation",()=>{
            var before=Before();
            foreach(var changes in new[]{new PropertyChanges("Layer",null,null,null),new PropertyChanges(null,null,"Dash",null)})
            {try{BlockRootProperties.Expected(before,changes,empty);}catch(InvalidOperationException){continue;}throw new Exception("Unresolved symbol accepted.");}
            Check(before.GetRawText()==Before().GetRawText());return Task.CompletedTask;
        });
    }
}
