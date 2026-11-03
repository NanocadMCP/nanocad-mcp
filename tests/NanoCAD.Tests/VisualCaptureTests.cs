using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class VisualCaptureTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        var bridge=Guid.NewGuid().ToString("D");var document=Guid.NewGuid().ToString("D");
        object Input(int edge=1600)=>new{bridge_instance_id=bridge,document_id=document,expected_revision="18446744073709551615",max_edge=edge};
        void Deny(Action action,string? code=null){try{action();}catch(DomainException e) when(code==null||e.Error.code==code){return;}throw new Exception("Unsafe visual input accepted");}
        await test("visual capture input preserves identity precision and rejects unrelated window controls",()=>{
            var args=VisualCaptureSchema.Parse(Wire.Element(Input()));
            if(args.BridgeId!=bridge||args.DocumentId!=document||args.Revision!="18446744073709551615"||args.MaxEdge!=1600)throw new Exception("Capture context changed");
            foreach(int edge in new[]{255,2049})Deny(()=>VisualCaptureSchema.Parse(Wire.Element(Input(edge))));
            var valid=JsonSerializer.SerializeToNode(Input())!;
            foreach(var key in new[]{"hwnd","path","pid","command","camera"}){var bad=valid.DeepClone();bad[key]="1";Deny(()=>VisualCaptureSchema.Parse(Wire.Element(bad)));}
            var overflow=valid.DeepClone();overflow["expected_revision"]="18446744073709551616";Deny(()=>VisualCaptureSchema.Parse(Wire.Element(overflow)));
            var fraction=valid.DeepClone();fraction["max_edge"]=512.5;Deny(()=>VisualCaptureSchema.Parse(Wire.Element(fraction)));return Task.CompletedTask;
        });
        await test("visual capture rejects stale document and changes during observation",()=>{
            var args=VisualCaptureSchema.Parse(Wire.Element(Input()));
            var before=Wire.Element(new{document_id=document,revision=args.Revision,active_space="model",path="own.dwg"});
            VisualCaptureService.RequireContext(args,before);VisualCaptureService.RequireUnchanged(before,before);
            Deny(()=>VisualCaptureService.RequireContext(args with{DocumentId=Guid.NewGuid().ToString("D")},before),"DOCUMENT_NOT_ACTIVE");
            Deny(()=>VisualCaptureService.RequireContext(args with{Revision="1"},before),"REVISION_MISMATCH");
            foreach(var changed in new[]{Wire.Element(new{document_id=document,revision="1",active_space="model",path="own.dwg"}),Wire.Element(new{document_id=document,revision=args.Revision,active_space="paper",path="own.dwg"})})
                Deny(()=>VisualCaptureService.RequireUnchanged(before,changed),"CAPTURE_CONTEXT_CHANGED");return Task.CompletedTask;
        });
        await test("visual capture image budgets reject oversized or inconsistent PNG",()=>{
            var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/6sAAAAASUVORK5CYII=");
            VisualCaptureService.RequirePng(png,1,1,256);
            var transferred=VisualCaptureService.DecodeImage(Wire.Element(new{png_base64=Convert.ToBase64String(png)}),1,1,256);
            if(!transferred.SequenceEqual(png))throw new Exception("IPC changed image bytes");
            Deny(()=>VisualCaptureService.DecodeImage(Wire.Element(new{}),1,1,256),"CAPTURE_UNAVAILABLE");
            Deny(()=>VisualCaptureService.DecodeImage(Wire.Element(new{png_base64="invalid!"}),1,1,256),"CAPTURE_FAILED");
            Deny(()=>VisualCaptureService.DecodeImage(Wire.Element(new{png_base64=new string('A',900000)}),1,1,256),"CAPTURE_IMAGE_LIMIT");
            Deny(()=>VisualCaptureService.RequirePng(png,2,1,256));Deny(()=>VisualCaptureService.RequirePng(png[..20],1,1,256));
            var over=new byte[VisualCaptureService.MaxPngBytes+1];png.CopyTo(over,0);Deny(()=>VisualCaptureService.RequirePng(over,1,1,256));return Task.CompletedTask;
        });
        await test("desktop capture bridge accepts only opaque bounded internal tokens",()=>{
            var token=Guid.NewGuid().ToString("N");
            var request=Wire.Element(new{bridge_instance_id=bridge,capture_id=token,max_edge=1600});
            var parsed=VisualCaptureBridgeArguments.Parse(request);
            if(!VisualCaptureBridgeArguments.Parse(Wire.Element(new{bridge_instance_id=bridge,capture_id=token,max_edge=1600,inline_image=true})).InlineImage)throw new Exception("Inline transport flag lost");
            if(parsed.BridgeId!=bridge||parsed.CaptureId!=token||parsed.MaxEdge!=1600||
               Wire.ValidateBridge("capture_window",request)!=bridge)throw new Exception("Internal capture identity changed");
            Deny(()=>VisualCaptureBridgeArguments.Parse(Wire.Element(new{bridge_instance_id=bridge,capture_id=token,max_edge=2049})));
            Deny(()=>VisualCaptureBridgeArguments.Parse(Wire.Element(new{bridge_instance_id=bridge,capture_id=token,max_edge=1600,hwnd="1"})));
            Deny(()=>VisualCaptureBridgeArguments.Parse(Wire.Element(new{bridge_instance_id=bridge,capture_id="../frame",max_edge=1600})));
            return Task.CompletedTask;
        });
        await test("visual capture inline image crosses the real IPC text-page budget",async()=>{
            var descriptor=Discovery.Describe(bridge,"capture-transport-test");
            string encoded=Convert.ToBase64String(new byte[600*1024]);
            using var listener=new BridgeListener(descriptor,(r,c)=>Task.FromResult(Result.Ok(r.requestId,new{png_base64=encoded})),new JsonLog("capture-test"));
            var request=new Request(Wire.Version,Guid.NewGuid().ToString("D"),"capture_window",9000,
                Wire.Element(new{bridge_instance_id=bridge,capture_id=Guid.NewGuid().ToString("N"),max_edge=1600,inline_image=true}));
            var response=await new BridgeClient().CallAsync(descriptor,request,CancellationToken.None);
            if(!response.success||Wire.Element(response.data!).GetProperty("png_base64").GetString()!=encoded)
                throw new Exception("Image was rejected or altered by IPC: "+response.error?.code);
        });
        await test("visual capture catalog exposes only the public observation tool",()=>{
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.capture_view");
            if(Wire.Version!="0.126"||Wire.Operations.Contains("capture_view")||Wire.Operations.Contains("capture_window")||
               Wire.Operations.Length!=20||ToolCatalog.Tools.Count!=22||tool.Annotations?.ReadOnlyHint!=true)
                throw new Exception("Internal capture endpoint became a public tool");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));return Task.CompletedTask;
        });
    }
}
