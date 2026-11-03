using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class SolidTopologyTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        Dictionary<string,object> Input()=>new(){["bridge_instance_id"]=Guid.NewGuid().ToString("D"),["document_id"]=Guid.NewGuid().ToString("D"),["expected_revision"]="18446744073709551615",["handle"]="000aB"};
        void Reject(object value){try{SolidTopologyValidation.Parse(Wire.Element(value));}catch(DomainException){return;}throw new Exception("Unsafe topology arguments accepted.");}
        await test("solid topology prototype requires complete current context and rejects mutation controls",()=>
        {
            var input=Input();var args=SolidTopologyValidation.Parse(Wire.Element(input));
            if(args.Handles![0]!="AB" || args.ExpectedRevision!=(string)input["expected_revision"] || args.DocumentId!=(string)input["document_id"])throw new Exception("Context or handle precision lost.");
            foreach(string field in input.Keys){var missing=Input();missing.Remove(field);Reject(missing);}
            foreach(string field in new[]{"regenerate","apply","subentity_id","handles"}){var extra=Input();extra[field]=true;Reject(extra);}
            foreach(string handle in new[]{"0","xyz","10000000000000000"}){var invalid=Input();invalid["handle"]=handle;Reject(invalid);}
            return Task.CompletedTask;
        });
        await test("solid topology catalog scopes native identities and permits explicitly unavailable curved face area",()=>
        {
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_solid_topology");
            if(!Wire.ReadOperations.Contains("inspect_solid_topology") || Wire.WriteOperations.Contains("inspect_solid_topology") || tool.Annotations?.ReadOnlyHint!=true)throw new Exception("Wrong read-only scope.");
            var schema=tool.OutputSchema!.Value.GetRawText();
            if(!schema.Contains("area_unavailable_reason") || !schema.Contains("snapshot_token") || !schema.Contains("bounds"))throw new Exception("Incomplete topology output contract.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));
            return Task.CompletedTask;
        });
        await test("native model face inventory is explicit and separate from B-rep identities",()=>
        {
            var input=Input();input["include_native_model_faces"]=true;
            var parsed=SolidTopologyValidation.Parse(Wire.Element(input));
            if(!parsed.IncludeNativeModelFaces||parsed.Handles![0]!="AB")throw new Exception("Native face option lost its exact source.");
            input["include_native_model_faces"]="true";Reject(input);
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_solid_topology");
            if(!tool.InputSchema.GetRawText().Contains("include_native_model_faces")||
               !tool.OutputSchema!.Value.GetRawText().Contains("model_face_id")||
               !tool.OutputSchema!.Value.GetRawText().Contains("face_token"))
                throw new Exception("Native and B-rep face identities were conflated in the contract.");
            return Task.CompletedTask;
        });
    }
}
