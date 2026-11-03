using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class BasicEditing335Tests
{
    private static JsonElement Request(object[] edits,string? space=null)=>Wire.Element(new{
        bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),operation_id=Guid.NewGuid().ToString("D"),
        expected_revision="1",units="Millimeters",target_space=space??"model",operations=edits});
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool value,string why){if(!value)throw new Exception(why);}
        void Reject(Action action,string code="INVALID_ARGUMENT")
        {try{action();}catch(DomainException error)when(error.Error.code==code){return;}throw new Exception("Invalid edit was accepted or gave an unrelated refusal.");}
        await test("block335 Model default and Paper block ownership remain distinct",()=>
        {
            object edit=new{operation="set_attribute_text",block_handle="a1",attribute_handle="b1",expected_text="Valve",text="Cover"};
            var modelInput=Request([edit]);var fields=modelInput.EnumerateObject().Where(p=>p.Name!="target_space").ToDictionary(p=>p.Name,p=>p.Value.Clone());
            var implicitModel=StructureWriteValidation.Parse(Wire.Element(fields));
            var explicitModel=StructureWriteValidation.Parse(modelInput);
            Need(implicitModel.TargetSpace==null&&implicitModel.EffectiveTargetSpace=="model"&&implicitModel.Fingerprint()==explicitModel.Fingerprint(),"Equivalent Model requests acquired different replay semantics.");
            Need(!Wire.Element(implicitModel).TryGetProperty("target_space",out _),"Default Model serialized an unnecessary wire field.");
            var paperFields=modelInput.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());
            paperFields["target_space"]=Wire.Element("paper");
            var paper=StructureWriteValidation.Parse(Wire.Element(paperFields));
            Need(paper.EffectiveTargetSpace=="paper"&&StructureWriteValidation.IsSettingsOnly(paper.Operations),"Literal sheet label edit lost its space or invented a units conversion.");
            Need(paper.Fingerprint()!=implicitModel.Fingerprint(),"Distinct ownership spaces share a replay fingerprint.");
            Reject(()=>StructureWriteValidation.Parse(Request([edit],"viewport")));
            return Task.CompletedTask;
        });
        await test("block335 viewport lifecycle stays explicit and has no structural count ceiling",()=>
        {
            object Copy(int i)=>new{operation="copy_viewport",layout_handle="a1",handle="b1",center=new[]{i*10.0,20.0},is_on=false};
            var many=StructureWriteValidation.Parse(Request(Enumerable.Range(0,80).Select(Copy).ToArray()));
            Need(many.Operations.Length==80&&many.Operations.All(op=>op is CopyViewportEdit{LayoutHandle:"A1",Handle:"B1",IsOn:false})&&WriteRecovery.NotGuaranteed(many),"Valid byte-bounded retained viewport copies were rejected or recovery was overstated.");
            var delete=new{operation="delete_viewport",layout_handle="a1",handle="b1"};
            var removal=StructureWriteValidation.Parse(Request([delete]));
            Need(StructureWriteValidation.IsSettingsOnly(removal.Operations)&&WriteRecovery.NotGuaranteed(removal),"Native deletion invents geometric units or guaranteed recovery.");
            Reject(()=>StructureWriteValidation.Parse(Request([Copy(0),delete])),"INVALID_OPERATION_ORDER");
            Reject(()=>StructureWriteValidation.Parse(Request([delete,delete])),"INVALID_OPERATION_ORDER");
            Reject(()=>StructureWriteValidation.Parse(Request([new{operation="copy_viewport",layout_handle="a1",handle="a1",center=new[]{1.0,2.0}}])));
            return Task.CompletedTask;
        });
        await test("block335 appended revolution retains semantic source axis and angle contract",()=>
        {
            object Revolve(double angle,string axis="c1",string mode="join")=>new{operation="add_model_revolution",handles=new[]{"a1"},
                expected_last_item_id=Guid.NewGuid().ToString("B"),part_operation=mode,source_handles=new[]{"b1","b2","b3","b4"},axis_handle=axis,
                plane=new{origin=new[]{0.0,0.0,0.0},x_axis=new[]{1.0,0.0,0.0},y_axis=new[]{0.0,1.0,0.0}},angle_degrees=angle};
            var result=ModifyValidation.Parse(Request([Revolve(0.001)]));
            Need(result.Operations.Single() is ModelFeatureRevolutionSpec{AngleDegrees:0.001,PartOperation:"join"}&&WriteRecovery.NotGuaranteed(result),"A valid small-angle native append was capped or recovery was overstated.");
            Need(EditReferences.All(result.Operations[0]).SequenceEqual(new[]{"A1","B1","B2","B3","B4","C1"}),"Read-only profile or axis dependencies are missing from the exact reference inventory.");
            ModifyValidation.Parse(Request([Revolve(360,mode:"cut")]));
            Reject(()=>ModifyValidation.Parse(Request([Revolve(0)])));
            Reject(()=>ModifyValidation.Parse(Request([Revolve(180,"b1")])));
            Reject(()=>ModifyValidation.Parse(Request([Revolve(180,mode:"new_body")])));
            Reject(()=>ModifyValidation.Parse(Request([Revolve(180),new{operation="erase",handles=new[]{"d1"}}])),"INVALID_OPERATION_ORDER");
            Reject(()=>ModifyValidation.Parse(Request([Revolve(180)],"paper")),"OUT_OF_SCOPE");
            return Task.CompletedTask;
        });
        await test("block335 agent sees native editing branches and additive result provenance",()=>
        {
            var structure=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_structure");
            Need(structure.InputSchema.GetRawText().Contains("copy_viewport")&&structure.InputSchema.GetRawText().Contains("delete_viewport")&&
                structure.InputSchema.GetProperty("properties").TryGetProperty("target_space",out _),"Sheet lifecycle/block targeting is hidden from the agent.");
            var output=structure.OutputSchema!.Value.GetRawText();
            Need(output.Contains("viewport_lifecycle_results"),"Returned native viewport provenance has no public schema.");
            var modify=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            Need(modify.InputSchema.GetRawText().Contains("add_model_revolution")&&modify.OutputSchema!.Value.GetRawText().Contains("model_revolution_features"),"Native history append or its references are unpublished.");
            foreach(var tool in ToolCatalog.Tools)Need(!tool.InputSchema.GetRawText().Contains("\"$ref\""),tool.Name+" exposes unresolved client input references.");
            Need(!structure.InputSchema.GetRawText().Contains("New335"),"Developer acceptance status leaked into runtime tool instructions.");
            return Task.CompletedTask;
        });
    }
}
