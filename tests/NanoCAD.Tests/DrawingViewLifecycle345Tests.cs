using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingViewLifecycle345Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool value,string message){if(!value)throw new Exception(message);}
        void Reject(Action action,string code="INVALID_ARGUMENT")
        {try{action();}catch(DomainException error)when(error.Error.code==code){return;}throw new Exception("Invalid view lifecycle request accepted.");}
        string token=new('a',64);
        object Target(string handle="A1",string? value=null)=>new{layout_handle="B1",handle,expected_name="Current projection",view_token=value??token};
        JsonElement Payload(params object[] operations)=>Wire.Element(new{bridge_instance_id="11111111-1111-1111-1111-111111111111",
            document_id="22222222-2222-2222-2222-222222222222",expected_revision="1",operation_id="33333333-3333-3333-3333-333333333333",
            units="Millimeters",target_space="paper",operations});
        await test("block345 view lifecycle binds current layout/token and explicit complete source group without body count ceilings",()=>
        {
            var sourceHandles=Enumerable.Range(1,129).Select(i=>i.ToString("X")).ToArray();
            var edit=StructureWriteValidation.Parse(Payload(new{operation="set_drawing_view_sources",targets=new[]{Target("A2"),Target()},source_handles=sourceHandles}));
            var sources=(SetDrawingViewSources345)edit.Operations.Single();
            Need(sources.SourceHandles.Length==129&&sources.Targets.Select(t=>t.Target.Handle).SequenceEqual(new[]{"A1","A2"})&&
                sources.Targets.All(t=>t.ViewToken==token.ToUpperInvariant())&&edit.EffectiveTargetSpace=="paper"&&StructureWriteValidation.IsSettingsOnly(edit.Operations),"Source/native freshness context was lost or capped.");
            var reordered=StructureWriteValidation.Parse(Payload(new{operation="set_drawing_view_sources",targets=new[]{Target(),Target("A2")},source_handles=sourceHandles.Reverse()}));
            Need(edit.Fingerprint()==reordered.Fingerprint(),"Explicit source/target sets should have stable canonical identity.");
            Reject(()=>StructureWriteValidation.Parse(Payload(new{operation="set_drawing_view_sources",targets=new[]{Target(),Target()},source_handles=new[]{"A3"}})));
            Reject(()=>StructureWriteValidation.Parse(Payload(new{operation="set_drawing_view_sources",targets=new[]{Target()},source_handles=new[]{"a3","A3"}})));
            Reject(()=>StructureWriteValidation.Parse(Payload(new{operation="set_drawing_view_sources",targets=Array.Empty<object>(),source_handles=new[]{"A3"}})));
            Reject(()=>StructureWriteValidation.Parse(Payload(new{operation="rename_drawing_view",layout_handle="B1",handle="A1",expected_name="Current projection",view_token="stale",name="New name"})));
            Reject(()=>StructureWriteValidation.Parse(Payload(new{operation="set_drawing_view_sources",targets=new[]{Target()},source_handles=new[]{"A3"}},new{operation="create_layer",name="Other"})),"INVALID_OPERATION_ORDER");
            return Task.CompletedTask;
        });
        await test("block345 child native direction and scale classification match sole published view lifecycle branches",()=>
        {
            object Child(string direction="iso_top_right",double scale=2)=>new{operation="add_drawing_view_child",layout_handle="B1",handle="A1",expected_name="Current projection",view_token=token,
                name="Native child",direction,origin=new[]{530d,100d},model_scale=scale};
            var child=StructureWriteValidation.Parse(Payload(Child()));
            Need(child.Operations.Single() is AddDrawingViewChild345 {Direction:"iso_top_right",ModelScale:2}&&!StructureWriteValidation.IsSettingsOnly(child.Operations),"Child placed geometry must retain drawing-unit admission.");
            var rename=StructureWriteValidation.Parse(Payload(new{operation="rename_drawing_view",layout_handle="B1",handle="A1",expected_name="Current projection",view_token=token,name="Section A"}));
            Need(StructureWriteValidation.IsSettingsOnly(rename.Operations),"Literal name-only operation should be unit independent.");
            Reject(()=>StructureWriteValidation.Parse(Payload(Child("front"))));Reject(()=>StructureWriteValidation.Parse(Payload(Child(scale:0))));
            var schema=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_structure").InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
            JsonElement Branch(string operation)=>schema.Single(s=>s.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()==operation);
            Need(!Branch("set_drawing_view_sources").GetProperty("properties").GetProperty("source_handles").TryGetProperty("maxItems",out _)&&
                Branch("rename_drawing_view").GetProperty("required").EnumerateArray().Any(v=>v.GetString()=="view_token")&&
                Branch("add_drawing_view_child").GetProperty("properties").GetProperty("direction").GetProperty("enum").EnumerateArray().Any(v=>v.GetString()=="iso_top_right"),"Published branches do not match source-set/token/native child directions.");
            return Task.CompletedTask;
        });
        await test("block345 selected section request preserves the old all-source form without claiming a global processing selector",()=>
        {
            object Section(string[] handles)=>new{operation="create_section_view",source_handles=handles,name="Selected section",section_line=new[]{new[]{10d,0d,0d},new[]{10d,20d,0d}},origin=new[]{530d,20d},scale=1};
            var selected=SectionViewValidation.Parse(Wire.Element(Section(["A1"])),"$.operations[0]");
            var all=SectionViewValidation.Parse(Wire.Element(Section(Enumerable.Range(1,129).Select(i=>i.ToString("X")).ToArray())),"$.operations[0]");
            Need(selected.SourceHandles.Length==1&&all.SourceHandles.Length==129&&selected.ViewingDirection==new Point3(1,0,0),"Selected/all source syntax or native section orientation changed.");
            Reject(()=>SectionViewValidation.Parse(Wire.Element(Section([])),"$.operations[0]"));
            Reject(()=>SectionViewValidation.Parse(Wire.Element(Section(["a1","A1"])),"$.operations[0]"));
            return Task.CompletedTask;
        });
    }
}
