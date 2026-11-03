using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DimensionEditing341Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool value,string why){if(!value)throw new Exception(why);}
        EditSpec Display(object p)=>DimensionEditingValidation341.Parse(Wire.Element(new{operation="set_dimension_display",handles=new[]{"A1"},properties=p}),"$.operations[0]",new[]{"A1"});
        void Reject(object p){try{Display(p);}catch(DomainException e)when(e.Error.code=="INVALID_ARGUMENT"){return;}throw new Exception("Invalid native display accepted.");}
        await test("block341 dimension display preserves native signed units and exact literal template intent",()=>
        {
            var edit=(DimensionDisplayEdit341)Display(new{linear_factor=-2.0,linear_unit_format=6,angular_unit_format=3,rounding=0.0,alternate_enabled=true,
                alternate_factor=.03937007874015748,alternate_unit_format=8,alternate_precision=3,linear_zero_suppression=12,angular_zero_suppression=3,
                primary_prefix="Ø",primary_suffix=" mm",alternate_prefix="",alternate_suffix=" in",text_gap=-.6,suppress_extension2=true});
            Need(edit.Properties.GetProperty("linear_factor").GetDouble()==-2&&edit.Properties.GetProperty("text_gap").GetDouble()==-.6&&edit.Properties.GetProperty("primary_prefix").GetString()=="Ø","Native values/affixes changed before dispatch.");
            using var raw=JsonDocument.Parse("{\"operation\":\"set_dimension_display\",\"handles\":[\"A1\"],\"properties\":{\"rounding\":0.0,\"linear_factor\":1e0}}");
            var lexical=(DimensionDisplayEdit341)DimensionEditingValidation341.Parse(raw.RootElement,"$",new[]{"A1"});
            Need(Wire.Element(DimensionEditingValidation341.NativeDisplayValue("rounding",lexical.Properties.GetProperty("rounding"))).GetRawText()=="0"&&
                 Wire.Element(DimensionEditingValidation341.NativeDisplayValue("linear_factor",lexical.Properties.GetProperty("linear_factor"))).GetRawText()=="1",
                 "Native expected readback retained irrelevant wire-number spelling.");
            Reject(new{});Reject(new{linear_unit_format=0});Reject(new{alternate_unit_format=9});Reject(new{angular_zero_suppression=4});
            Reject(new{alternate_factor=0});Reject(new{rounding=-.1});Reject(new{primary_prefix="x"});Reject(new{primary_prefix="%%c",primary_suffix=""});
            Reject(new{alternate_prefix="[]",alternate_suffix=""});Reject(new{primary_prefix="\\H2;x",primary_suffix=""});Reject(new{arbitrary=1});
            return Task.CompletedTask;
        });
        await test("block341 full native definitions bind subtype and preserve physical line arc placement",()=>
        {
            JsonElement aligned=Wire.Element(new{xline1_point=new[]{0.0,0,0},xline2_point=new[]{10.0,0,0},dimline_point=new[]{0.0,3,0}});
            var native=Wire.Element(new{xline1_point=new[]{0.0,0,0},xline2_point=new[]{10.0,0,0},dimline_point=new[]{7.0,3,0}});
            Need(DimensionEditingValidation341.SameDefinition(aligned,native),"Native tangential DimLinePoint normalization was rejected.");
            Need(!DimensionEditingValidation341.SameDefinition(aligned,Wire.Element(new{xline1_point=new[]{0.0,0,0},xline2_point=new[]{10.0,0,0},dimline_point=new[]{0.0,4,0}})),"Wrong physical dimension offset accepted.");
            JsonElement Arc(double x,double y)=>Wire.Element(new{center_point=new[]{0.0,0,0},xline1_point=new[]{10.0,0,0},xline2_point=new[]{0.0,10,0},arc_point=new[]{x,y,0}});
            Need(DimensionEditingValidation341.SameDefinition(Arc(3,4),Arc(4,3))&&!DimensionEditingValidation341.SameDefinition(Arc(3,4),Arc(-4,3))&&
                 !DimensionEditingValidation341.SameDefinition(Arc(3,4),Arc(6,8)),"Native arc radius/selected sector not retained.");
            var edit=DimensionEditingValidation341.Parse(Wire.Element(new{operation="set_dimension_definition",handles=new[]{"A1"},dimension_kind="AlignedDimension",expected_definition=aligned,definition=native}),"$",new[]{"A1"});
            Need(edit is DimensionDefinitionEdit341 {DimensionKind:"AlignedDimension"},"Full native definition lost its subtype.");
            var other=new DimensionDisplayEdit341(new[]{"B2"},Wire.Element(new{rounding=0}));DimensionEditingValidation341.Batch(new[]{edit,other});
            try{DimensionEditingValidation341.Batch(new[]{edit,Display(new{rounding=0})});throw new Exception("Same-root mixed edits accepted.");}catch(DomainException e)when(e.Error.code=="INVALID_OPERATION_ORDER"){}
            foreach(string kind in DimensionEditingValidation341.Kinds)Need(DimensionEditingValidation341.DefinitionNames(kind).Length>=3,"Missing native subtype definition.");
            return Task.CompletedTask;
        });
        await test("block341 dimension read publication observes native values without edit range gates",()=>
        {
            var read=DimensionEditingSchema341.ReadDisplay();var props=read["properties"]!.AsObject();
            Need(props["linear_factor"]!["type"]!.GetValue<string>()=="number"&&props["linear_factor"]!["maximum"]==null&&props["linear_unit_format"]!["maximum"]==null,"Request range was incorrectly applied to existing native reads.");
            Need(props.ContainsKey("primary_template")&&props.ContainsKey("alternate_template")&&!props.ContainsKey("primary_prefix"),"Stored template cannot be reconstructed by guessing literal affixes.");
            var geometry=DimensionEditingSchema341.DefinitionOperation();Need(geometry["oneOf"]!.AsArray().Count==7,"Subtype-specific definition schema omitted a native family.");
            var display=DimensionEditingSchema341.DisplayOperation();Need(display["properties"]!["properties"]!["properties"]!.AsObject().Count==DimensionEditingValidation341.DisplayNames.Length,"Display parser/schema fields diverged.");
            return Task.CompletedTask;
        });
    }
}
