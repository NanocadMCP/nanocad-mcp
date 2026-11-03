using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class IndependentContourCorners343Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool valid,string message){if(!valid)throw new Exception(message);}
        void Reject(Action action,string code="INVALID_ARGUMENT")
        {try{action();}catch(DomainException e)when(e.Error.code==code){return;}throw new Exception("Invalid independent corner request accepted.");}
        string a=new('A',64),b=new('B',64);
        object Fillet()=>new{operation="fillet_curves",handles=new[]{"b2","a1"},expected_curve_tokens=new[]{b.ToLowerInvariant(),a},ends=new[]{"end","start"},trim_sources=true,radius=.0001};
        await test("block343 ordered corner bindings and measured setbacks keep native parameter semantics separate",()=>
        {
            var spec=IndependentContourCornersValidation343.Parse(Wire.Element(Fillet()),"$.operations[0]",["A1","B2"],"fillet_curves");
            Need(spec.Handles.SequenceEqual(new[]{"B2","A1"})&&spec.ExpectedCurveTokens.SequenceEqual(new[]{b,a})&&spec.Ends.SequenceEqual(new[]{"end","start"})&&spec.Radius==.0001&&spec.TrimSources,"Generic source-set order must not rebind chosen ends, tokens or distances.");
            Need(IndependentContourCornersValidation343.TargetDistance("start",14,44,6)==20&&IndependentContourCornersValidation343.TargetDistance("end",14,44,8)==36,"Native distances are measured from actual start/end distances, not treated as parameters or presumed zero-origin distances.");
            Reject(()=>IndependentContourCornersValidation343.TargetDistance("end",14,44,30),"INVALID_CORNER_SETBACK");
            Reject(()=>IndependentContourCornersValidation343.TargetDistance("start",14,44,double.PositiveInfinity),"INVALID_CORNER_SETBACK");
            Reject(()=>IndependentContourCornersValidation343.Parse(Wire.Element(new{operation="chamfer_curves",handles=new[]{"A1","a1"},expected_curve_tokens=new[]{a,b},ends=new[]{"end","start"},trim_sources=false,distances=new[]{6,8}}),"$",["A1","A1"],"chamfer_curves"));
            Reject(()=>IndependentContourCornersValidation343.Parse(Wire.Element(new{operation="fillet_curves",handles=new[]{"A1","B2"},expected_curve_tokens=new[]{a,b},ends=new[]{"end","start"},trim_sources=true,radius=5,erase_sources=true}),"$",["A1","B2"],"fillet_curves"));
            return Task.CompletedTask;
        });
        await test("block343 published pair provenance and sole native corner route retain source identities",()=>
        {
            JsonElement Payload(params object[] operations)=>Wire.Element(new{bridge_instance_id=Guid.NewGuid().ToString(),document_id=Guid.NewGuid().ToString(),expected_revision="1",operation_id=Guid.NewGuid().ToString(),units="Millimeters",target_space="paper",operations});
            var edit=(NativeCornerCurveSpec343)ModifyValidation.Parse(Payload(Fillet())).Operations.Single();Need(edit.Handles[0]=="B2"&&edit.Ends[0]=="end","Actual root parser/Paper dispatch lost explicit pair ordering.");
            Reject(()=>ModifyValidation.Parse(Payload(Fillet(),new{operation="erase",handles=new[]{"C3"}})),"INVALID_OPERATION_ORDER");
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");var branches=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
            foreach(string kind in new[]{"fillet_curves","chamfer_curves"})
            {
                var matching=branches.Where(v=>v.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()==kind).ToArray();Need(matching.Length==1,"Missing/ambiguous corner branch.");
                var p=matching[0].GetProperty("properties");Need(p.GetProperty("handles").GetProperty("minItems").GetInt32()==2&&p.GetProperty("ends").GetProperty("maxItems").GetInt32()==2,"Pair contract lost.");
                Need(matching[0].GetProperty("required").EnumerateArray().Any(v=>v.GetString()=="trim_sources")&&p.GetProperty("expected_curve_tokens").GetProperty("items").GetProperty("pattern").GetString()=="^[0-9A-Fa-f]{64}$","Native source binding/policy missing.");
            }
            var result=Wire.Element(new ContourCornerResult343(0,"fillet_curves",[new("bridge","doc","A1"),new("bridge","doc","B2")],["end","start"],new("bridge","doc","C3"),true,[[25,0,0],[30,5,0]],[25,.5],[5,5],5));
            Need(result.GetProperty("sources").GetArrayLength()==2&&result.GetProperty("ends")[1].GetString()=="start"&&result.GetProperty("connector").GetProperty("handle").GetString()=="C3","Result must describe both original sources and one actual connector, not masquerade as a copy.");
            return Task.CompletedTask;
        });
    }
}
