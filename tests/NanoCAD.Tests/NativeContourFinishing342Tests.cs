using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class NativeContourFinishing342Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool valid,string message){if(!valid)throw new Exception(message);}
        void Reject(Action action,string code="INVALID_ARGUMENT")
        {try{action();}catch(DomainException e)when(e.Error.code==code){return;}throw new Exception("Invalid native finishing accepted.");}
        string token=new('A',64);
        NativeContourFinishingSpec342 Parse(object value,string kind)=>NativeContourFinishingValidation342.Parse(Wire.Element(value),"$.operations[0]",["A1"],kind);
        await test("block342 native finishing parameters, WCS pick and exact source token remain explicit",()=>
        {
            var cuts=Enumerable.Range(1,80).Select(i=>i/100d).ToArray();
            var split=Parse(new{operation="split_curve",mode="native",handles=new[]{"A1"},expected_curve_token=token,cut_parameters=cuts,erase_sources=false},"split_curve");
            Need(split.CutParameters.SequenceEqual(cuts)&&!split.EraseSource,"No whole-curve cut ceiling or distance reinterpretation.");
            var trim=Parse(new{operation="trim_curve",mode="native",handles=new[]{"A1"},expected_curve_token=token,boundary_handles=new[]{"A2","A3"},pick_point_wcs=new[]{2d,3d,4d}},"trim_curve");
            Need(trim.EraseSource&&trim.PickPointWcs==new Point3(2,3,4)&&trim.BoundaryHandles.SequenceEqual(new[]{"A2","A3"}),"Native trim source replacement/3D point/boundary refs lost.");
            var extend=Parse(new{operation="extend_curve",mode="native",handles=new[]{"A1"},expected_curve_token=token,end="start",target_point_wcs=new[]{5d,6d,7d}},"extend_curve");
            Need(extend.End=="start"&&!extend.EraseSource&&extend.TargetPointWcs==new Point3(5,6,7),"Native stored endpoint meaning lost.");
            Reject(()=>Parse(new{operation="split_curve",mode="native",handles=new[]{"A1"},expected_curve_token=token,cut_parameters=new[]{.5,.5},erase_sources=true},"split_curve"));
            Reject(()=>Parse(new{operation="trim_curve",mode="native",handles=new[]{"A1"},expected_curve_token=token,boundary_handles=new[]{"A1"},pick_point_wcs=new[]{0d,0d,0d}},"trim_curve"));
            Reject(()=>Parse(new{operation="extend_curve",mode="native",handles=new[]{"A1"},expected_curve_token="old",end="end",target_point_wcs=new[]{0d,0d,0d}},"extend_curve"));
            Reject(()=>Parse(new{operation="extend_curve",mode="native",handles=new[]{"A1"},expected_curve_token=token,end="end",target_point_wcs=new[]{0d,0d},guess_parameter=.5},"extend_curve"));
            return Task.CompletedTask;
        });
        await test("block342 actual published finishing variants retain legacy and native sole request dispatch",()=>
        {
            var context=new{bridge_instance_id=Guid.NewGuid().ToString(),document_id=Guid.NewGuid().ToString(),expected_revision="1",operation_id=Guid.NewGuid().ToString(),units="Millimeters",target_space="paper"};
            JsonElement Payload(params object[] operations)=>Wire.Element(new{context.bridge_instance_id,context.document_id,context.expected_revision,context.operation_id,context.units,context.target_space,operations});
            var native=new{operation="split_curve",mode="native",handles=new[]{"A1"},expected_curve_token=token,cut_parameters=new[]{.5},erase_sources=false};
            Need(ModifyValidation.Parse(Payload(native)).Operations.Single() is NativeContourFinishingSpec342,"Actual parser native split/Paper hook missing.");
            Need(ModifyValidation.Parse(Payload(new{operation="trim_curve",handles=new[]{"A1"},boundary_handles=new[]{"A2"},pick_point=new[]{1,2}})).Operations.Single() is TrimCurveSpec,"Legacy trim rerouted.");
            Reject(()=>ModifyValidation.Parse(Payload(native,new{operation="erase",handles=new[]{"A2"}})),"INVALID_OPERATION_ORDER");
            var branches=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
            foreach(string kind in new[]{"trim_curve","split_curve","extend_curve"})
            {
                var matching=branches.Where(b=>b.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()==kind).ToArray();
                Need(matching.Length==1&&matching[0].GetProperty("oneOf").GetArrayLength()==2,"Ambiguous/missing complete legacy/native variant "+kind);
                var arm=matching[0].GetProperty("oneOf")[1];Need(arm.GetProperty("properties").GetProperty("mode").GetProperty("const").GetString()=="native"&&arm.GetProperty("required").EnumerateArray().Any(v=>v.GetString()=="expected_curve_token"),"Native precondition absent.");
            }
            return Task.CompletedTask;
        });
    }
}
