using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class LayerRenameTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        var b=Guid.NewGuid().ToString("D");var d=Guid.NewGuid().ToString("D");var id=Guid.NewGuid().ToString("D");
        object Edit(string handle="AF",string expected="Old layer",string name="New layer")=>new{operation="rename_layer",handle,expected_name=expected,name};
        StructureWriteArguments Parse(params object[] operations)=>StructureWriteValidation.Parse(Wire.Element(new{bridge_instance_id=b,document_id=d,operation_id=id,expected_revision="9",units="Millimeters",operations}));
        void Check(bool ok){if(!ok)throw new Exception("Layer rename assertion failed.");}
        void Deny(Action action){try{action();}catch(DomainException){return;}throw new Exception("Invalid layer rename accepted.");}
        await test("layer rename retains identity exact expectation unicode destination and replay order",()=>{
            var a=Parse(Edit());var edit=(RenameLayerEdit)a.Operations.Single();Check(edit.Handle=="AF"&&edit.ExpectedName=="Old layer"&&edit.Name=="New layer");
            Check(a.Fingerprint()==Parse(Edit(handle:"00af")).Fingerprint());Check(a.Fingerprint()!=Parse(Edit(expected:"Old LAYER")).Fingerprint());Check(a.Fingerprint()!=Parse(Edit(name:"New")).Fingerprint());
            var second=Edit("B0","A","B");Check(Parse(Edit(),second).Fingerprint()!=Parse(second,Edit()).Fingerprint());return Task.CompletedTask;
        });
        await test("layer rename rejects reserved names case only duplicates mixed batches and excessive work",()=>{
            foreach(var name in new[]{"0","Defpoints","X|Y","*Special","Trailing ","A/B"})Deny(()=>Parse(Edit(name:name)));
            Deny(()=>Parse(Edit(expected:"Layer",name:"LAYER")));Deny(()=>Parse(Edit(),Edit("AF","B","C")));Deny(()=>Parse(Edit(name:"Same"),Edit("B0","B","SAME")));
            Deny(()=>Parse(Edit(),new{operation="create_layer",name="Fresh"}));Deny(()=>Parse(Edit(),new{operation="set_layer_properties",handle="B0",properties=new{color_aci=3}}));
            var many=Enumerable.Range(1,8).Select(i=>Edit(i.ToString("X"),"Old"+i,"New"+i)).ToArray();Parse(many);Deny(()=>Parse(many.Append(Edit("F","OldF","NewF")).ToArray()));return Task.CompletedTask;
        });
        await test("layer rename preflight distinguishes changed name conflict and dependent sources",()=>{
            var e=new RenameLayerEdit("AF","Original","New");LayerRenameValidation.Source("Original",false,e,false);
            foreach(var entry in new[]{("ORIGINAL",false,false,"LAYER_NAME_CHANGED"),("Original",false,true,"LAYER_EXISTS"),("Original",true,false,"LAYER_UNAVAILABLE"),("0",false,false,"LAYER_UNAVAILABLE")})
            {try{LayerRenameValidation.Source(entry.Item1,entry.Item2,e,entry.Item3);}catch(DomainException ex)when(ex.Error.code==entry.Item4){continue;}throw new Exception("Wrong source guard.");}
            return Task.CompletedTask;
        });
        await test("layer rename schema remains within the existing structural tool",()=>{
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_structure");var variants=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf");
            var rename=variants.EnumerateArray().Single(v=>v.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="rename_layer");
            Check(ToolCatalog.Tools.Count==ToolCatalog.OperationNames.Length&&rename.GetProperty("required").GetArrayLength()==4&&rename.GetProperty("properties").GetProperty("name").GetProperty("maxLength").GetInt32()==255);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));return Task.CompletedTask;
        });
    }
}
