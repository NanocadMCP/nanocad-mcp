using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class CurveDistanceTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        object Input(string[] handles,string mode="distance",object? point=null)=>point==null
            ?new{bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),expected_revision="5",mode,handles}
            :new{bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),expected_revision="5",mode,handles,point};
        void Reject(object input){try{GeometryAnalysisValidation.Parse(Wire.Element(input));}catch(DomainException){return;}throw new Exception("Unsafe distance input accepted.");}
        await test("curve distance requires ordered distinct pair and rejects point projection controls",()=>{
            var args=GeometryAnalysisValidation.Parse(Wire.Element(Input(new[]{"0b","0a"})));
            if(args.Mode!="distance"||args.Point!=null||!args.Read.Handles!.SequenceEqual(new[]{"B","A"}))throw new Exception("Distance pair order lost.");
            Reject(Input(new[]{"A"}));Reject(Input(new[]{"A","B","C"}));Reject(Input(new[]{"a","0A"}));Reject(Input(new[]{"A","B"},point:new[]{0,0,0}));
            var json=JsonSerializer.Deserialize<Dictionary<string,object>>(JsonSerializer.Serialize(Input(new[]{"A","B"})))!;json["extend"]=true;Reject(json);
            return Task.CompletedTask;
        });
        await test("curve distance verification rejects extended witnesses inconsistent scalars and nonfinite results",()=>{
            var first=new[]{1d,2d,3d};var second=new[]{4d,6d,3d};
            if(CurveDistanceVerification.Verify(first,second,5,first,second)!=5)throw new Exception("3-4-5 pair changed.");
            void Fail(double[] a,double[] b,double d,double[] pa,double[] pb){try{CurveDistanceVerification.Verify(a,b,d,pa,pb);}catch(DomainException e)when(e.Error.code=="NATIVE_DISTANCE_MISMATCH"){return;}throw new Exception("Invalid native distance accepted.");}
            Fail(first,second,4.9,first,second);Fail(first,second,-5,first,second);Fail(first,second,double.NaN,first,second);
            Fail(first,second,5,new[]{1d,2d,4d},second);Fail(first,second,5,first,new[]{4d,5d,3d});
            Fail(new[]{double.NaN,0,0},second,5,first,second);Fail(first,new[]{1d,2d},5,first,second);
            // An absolute witness tolerance must not grow with world offsets.
            var farA=new[]{900000000d,0d,0d};var farB=new[]{900000003d,4d,0d};
            Fail(farA,farB,5,new[]{900000000d,0.001,0d},farB);
            if(CurveDistanceVerification.Verify(first,first,0,first,first)!=0)throw new Exception("Zero-distance nonunique case rejected.");
            return Task.CompletedTask;
        });
        await test("curve distance schema preserves read scope and declares finite nonunique closest pair",()=>{
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.analyze_geometry");
            var modes=tool.InputSchema.GetProperty("properties").GetProperty("mode").GetProperty("enum").EnumerateArray().Select(m=>m.GetString()).ToArray();
            var variant=tool.OutputSchema!.Value.GetProperty("properties").GetProperty("data").GetProperty("anyOf")[0].GetProperty("oneOf").EnumerateArray().Single(v=>v.GetProperty("properties").GetProperty("mode").GetProperty("const").GetString()=="distance");
            var props=variant.GetProperty("properties");
            if(!props.GetProperty("solver").GetProperty("enum").EnumerateArray().Select(v=>v.GetString()).SequenceEqual(new[]{"native_kernel","analytic_finite_segments","analytic_segment_circle","analytic_segment_arc"}))throw new Exception("Distance solver outputs missing from schema.");
            if(!modes.Contains("distance")||ToolCatalog.Tools.Count!=ToolCatalog.OperationNames.Length||tool.Annotations?.ReadOnlyHint!=true||props.GetProperty("distance").GetProperty("minimum").GetInt32()!=0||props.GetProperty("overlap_evaluated").GetProperty("const").GetBoolean()||!props.TryGetProperty("first_point",out _)||!props.TryGetProperty("second_point",out _))throw new Exception("Distance contract boundary changed.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));return Task.CompletedTask;
        });
    }
}
