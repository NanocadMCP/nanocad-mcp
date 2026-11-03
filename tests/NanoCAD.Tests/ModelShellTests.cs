using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelShellTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        var last="{"+Guid.NewGuid().ToString("D").ToUpperInvariant()+"}";
        var token=new string('A',64);
        Dictionary<string,object> Shell()=>new()
        {
            ["operation"]="add_model_shell",["handles"]=new[]{"AB"},
            ["expected_last_item_id"]=last,["face_token"]=token,
            ["open_face_id"]="{FACE-CURRENT-NATIVE-ID}",["thickness"]=2.5
        };
        Dictionary<string,object> Change()=>new()
        {
            ["operation"]="set_model_shell_thickness",["handles"]=new[]{"AB"},
            ["expected_last_item_id"]=last,["feature_id"]=last,
            ["expected_thickness"]=2.5,["thickness"]=3.0
        };
        ModifyArguments Parse(params object[] edits)=>ModifyValidation.Parse(Wire.Element(new
        {
            bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),
            operation_id=Guid.NewGuid().ToString("D"),expected_revision="9",units="Millimeters",operations=edits
        }));
        void Reject(params object[] edits)
        {try{Parse(edits);throw new Exception("Unsafe native shell request accepted.");}catch(DomainException){}}
        await test("native shell contract binds one current MultiCAD face and warns about uncertain writes",()=>
        {
            var args=Parse(Shell());
            if(args.Operations.Single() is not ModelShellSpec shell||!shell.OpenFaceIds.SequenceEqual(new[]{"{FACE-CURRENT-NATIVE-ID}"})||
               shell.Thickness!=2.5||!WriteRecovery.NotGuaranteed(args))throw new Exception("Shell intent or recovery contract lost.");
            var schema=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema.GetRawText();
            if(!schema.Contains("add_model_shell")||!schema.Contains("open_face_id")||!schema.Contains("face_token"))
                throw new Exception("Native shell selector absent from tool schema.");
            return Task.CompletedTask;
        });
        await test("native shell rejects stale or ambiguous requests before CAD write",()=>
        {
            var stale=Shell();stale["face_token"]="stale";Reject(stale);
            var zero=Shell();zero["thickness"]=0;Reject(zero);
            var many=Shell();many["handles"]=new[]{"AB","CD"};Reject(many);
            var extra=Shell();extra["teigha_face_id"]="4";Reject(extra);
            Reject(Shell(),Shell());
            return Task.CompletedTask;
        });
        await test("native shell thickness edit requires current model feature and changed value",()=>
        {
            var args=Parse(Change());
            if(args.Operations.Single() is not ModelShellThicknessSpec edit||edit.ExpectedThickness!=2.5||
                edit.Thickness!=3.0||!WriteRecovery.NotGuaranteed(args))throw new Exception("Native shell edit contract lost.");
            var other=Change();other["feature_id"]="bad";Reject(other);
            var same=Change();same["thickness"]=2.5;Reject(same);
            var bad=Change();bad["expected_thickness"]=0;Reject(bad);
            Reject(Change(),Shell());
            var schema=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema.GetRawText();
            if(!schema.Contains("set_model_shell_thickness")||!schema.Contains("expected_thickness"))
                throw new Exception("Native thickness edit absent from tool schema.");
            return Task.CompletedTask;
        });
    }
}
