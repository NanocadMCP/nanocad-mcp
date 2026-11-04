using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class NativeContourOffset341Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool condition,string message){if(!condition)throw new Exception(message);}
        NativeOffsetCurveSpec341 Parse(string side,double distance)=>NativeContourOffsetValidation341.Parse(
            Wire.Element(new{operation="offset_curve",handles=new[]{"A1"},side,distance}),"$.operations[0]",new[]{"A1"});
        void Reject(Action action,string code="INVALID_ARGUMENT")
        {try{action();}catch(DomainException e)when(e.Error.code==code){return;}throw new Exception("Invalid native offset accepted.");}
        await test("block341 native offset retains explicit signed distance without a legacy minimum floor",()=>
        {
            Need(Parse("native_positive",.00001).SignedDistance==.00001&&Parse("native_negative",2.5).SignedDistance==-2.5,
                "Native kernel sign/magnitude intent lost.");
            Reject(()=>Parse("native_positive",0));Reject(()=>Parse("native_positive",-1));Reject(()=>Parse("native_negative",1e7));
            Reject(()=>Parse("outward",1));Reject(()=>Parse("Native_Positive",1));
            Reject(()=>NativeContourOffsetValidation341.Parse(Wire.Element(new{operation="offset_curve",handles=new[]{"A1"},side="native_positive",distance=1,guess_inside=true}),"$",new[]{"A1"}));
            Reject(()=>NativeContourOffsetValidation341.Parse(Wire.Element(new{operation="offset_curve",handles=new[]{"A1","A2"},side="native_positive",distance=1}),"$",new[]{"A1","A2"}));
            return Task.CompletedTask;
        });
        await test("block341 one actual published offset variant preserves legacy dispatch and native sole request",()=>
        {
            var context=new{bridge_instance_id=Guid.NewGuid().ToString(),document_id=Guid.NewGuid().ToString(),expected_revision="1",operation_id=Guid.NewGuid().ToString(),units="Millimeters",target_space="paper"};
            JsonElement Payload(params object[] operations)=>Wire.Element(new{context.bridge_instance_id,context.document_id,context.expected_revision,
                context.operation_id,context.units,context.target_space,operations});
            var native=ModifyValidation.Parse(Payload(new{operation="offset_curve",handles=new[]{"A1"},side="native_negative",distance=.00001}));
            Need(native.TargetSpace=="paper"&&native.Operations.Single() is NativeOffsetCurveSpec341{SignedDistance:-.00001},"Actual parse/Paper route differs.");
            var legacy=ModifyValidation.Parse(Payload(new{operation="offset_curve",handles=new[]{"A1"},side="left",distance=5}));
            Need(legacy.Operations.Single() is OffsetCurveSpec{Side:"left",Distance:5},"Legacy offset was rerouted.");
            var explicitLegacy=ModifyValidation.Parse(Payload(new{operation="offset_curve",mode="legacy",handles=new[]{"A1"},side="left",distance=5}));
            Need(explicitLegacy.Fingerprint()==legacy.Fingerprint(),"Explicit legacy mode changed existing behavior or request identity.");
            var signed=ModifyValidation.Parse(Payload(new{operation="offset_curve",handles=new[]{"A1"},signed_distance=-.00001}));
            Need(signed.Fingerprint()==native.Fingerprint(),"Signed native distance did not select the same existing native behavior.");
            Reject(()=>ModifyValidation.Parse(Payload(new{operation="offset_curve",handles=new[]{"A1"},signed_distance=-2d,side="native_negative",distance=2d})));
            Reject(()=>ModifyValidation.Parse(Payload(new{operation="offset_curve",mode="legacy",handles=new[]{"A1"},signed_distance=-2d})));
            Reject(()=>ModifyValidation.Parse(Payload(new{operation="offset_curve",mode="native",handles=new[]{"A1"},side="left",distance=5d})));
            Reject(()=>ModifyValidation.Parse(Payload(new{operation="offset_curve",handles=new[]{"A1"},side="left",distance=.00001})),"INVALID_GEOMETRY");
            Reject(()=>ModifyValidation.Parse(Payload(new{operation="offset_curve",handles=new[]{"A1"},side="native_positive",distance=2},new{operation="erase",handles=new[]{"A2"}})),"INVALID_OPERATION_ORDER");
            var variants=ToolCatalog.Tools.Single(tool=>tool.Name=="nanocad.modify_entities").InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf")
                .EnumerateArray().Where(branch=>branch.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="offset_curve").ToArray();
            Need(variants.Length==1,"Ambiguous offset guidance branches.");
            var schema=variants[0];var sides=schema.GetProperty("properties").GetProperty("side").GetProperty("enum").EnumerateArray().Select(v=>v.GetString()).ToArray();
            Need(sides.SequenceEqual(new[]{"left","right","inward","outward","native_positive","native_negative"})&&
                schema.GetProperty("properties").GetProperty("distance").GetProperty("exclusiveMinimum").GetInt32()==0&&
                schema.GetProperty("allOf")[0].GetProperty("then").GetProperty("properties").GetProperty("distance").GetProperty("minimum").GetDouble()==.001,
                "Actual published old/native distance/sign semantics differ.");
            return Task.CompletedTask;
        });
    }
}
