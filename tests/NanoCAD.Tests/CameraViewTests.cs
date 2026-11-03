using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;
using NanoCAD.Ipc;

internal static class CameraViewTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        var bridge=Guid.NewGuid().ToString("D");var document=Guid.NewGuid().ToString("D");
        void Deny(Action action,string code){try{action();}catch(DomainException e) when(e.Error.code==code){return;}throw new Exception("Expected "+code);}
        CameraView View(string b,string d,string r,int vp=2,double twist=0)=>CameraViews.Create(b,d,r,vp,null,[0,0,0],[1,-1,1],[0,0],100,80,twist,0,0);
        await test("camera metadata tokens bind lifetime document revision viewport and view",()=>{
            var view=View(bridge,document,"18446744073709551615");
            if(view.camera_token.Length!=64 || View(bridge,document,"18446744073709551615").camera_token!=view.camera_token)throw new Exception("Token is unstable");
            foreach(var changed in new[]{View(Guid.NewGuid().ToString("D"),document,"18446744073709551615"),View(bridge,Guid.NewGuid().ToString("D"),"18446744073709551615"),View(bridge,document,"1"),View(bridge,document,"18446744073709551615",3),View(bridge,document,"18446744073709551615",2,.1)})
                if(changed.camera_token==view.camera_token)throw new Exception("Cross-context token collision");
            Deny(()=>CameraViews.Create(bridge,document,"1",2,null,[0,0,0],[0,0,0],[0,0],100,80,0,0,0),"VIEW_UNAVAILABLE");
            Deny(()=>CameraViews.Create(bridge,document,"1",2,null,[0,0,0],[0,0,1],[0,0],double.NaN,80,0,0,0),"VIEW_UNAVAILABLE");
            return Task.CompletedTask;
        });
        await test("camera read is opt in and capture refuses stale or missing camera evidence",()=>{
            if(Validation.Parse("get_document",Wire.Element(new{})).IncludeView || !Validation.Parse("get_document",Wire.Element(new{include_view=true})).IncludeView)throw new Exception("Camera opt-in broken");
            Deny(()=>Validation.Parse("get_document",Wire.Element(new{include_view="true"})),"INVALID_ARGUMENT");
            using(var duplicate=JsonDocument.Parse("{\"include_view\":true,\"include_view\":false}"))Deny(()=>Validation.Parse("get_document",duplicate.RootElement),"INVALID_ARGUMENT");
            var view=View(bridge,document,"1");
            var args=VisualCaptureSchema.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=document,expected_revision="1",expected_camera_token=view.camera_token}));
            var before=Wire.Element(new{document_id=document,revision="1",view});
            VisualCaptureService.RequireContext(args,before);
            Deny(()=>VisualCaptureService.RequireContext(args,Wire.Element(new{document_id=document,revision="1"})),"CAMERA_CONTEXT_CHANGED");
            var changed=Wire.Element(new{document_id=document,revision="1",view=View(bridge,document,"1",2,.1)});
            Deny(()=>VisualCaptureService.RequireContext(args,changed),"CAMERA_CONTEXT_CHANGED");
            Deny(()=>VisualCaptureService.RequireUnchanged(before,changed),"CAPTURE_CONTEXT_CHANGED");
            return Task.CompletedTask;
        });
        await test("camera change allowlist and ledger preserve exact view intent",()=>{
            var id=Guid.NewGuid().ToString("D");var token=View(bridge,document,"1").camera_token;
            var input=Wire.Element(new{bridge_instance_id=bridge,document_id=document,expected_revision="1",operation_id=id,expected_camera_token=token,action="preset",preset="isometric",display_style="shaded_edges"});
            var args=CameraChangeValidation.Parse(input);
            if(args.DisplayStyle!="shaded_edges"||args.Preset!="isometric")throw new Exception("Camera intent changed");
            var bad=JsonSerializer.SerializeToNode(input)!;bad["command"]="_ZOOM";Deny(()=>CameraChangeValidation.Parse(Wire.Element(bad)),"INVALID_ARGUMENT");
            bad=JsonSerializer.SerializeToNode(input)!;bad["action"]="restore";bad["restore_token"]=Guid.NewGuid().ToString("D");Deny(()=>CameraChangeValidation.Parse(Wire.Element(bad)),"INVALID_ARGUMENT");
            bad=JsonSerializer.SerializeToNode(input)!;bad["display_style"]="realistic";Deny(()=>CameraChangeValidation.Parse(Wire.Element(bad)),"INVALID_ARGUMENT");
            using var queue=new WriteQueue(Environment.CurrentManagedThreadId);
            var request=new Request(Wire.Version,Guid.NewGuid().ToString("D"),"set_view",8000,input);
            var first=queue.Enqueue(request,CancellationToken.None);var replay=queue.Enqueue(request with{requestId=Guid.NewGuid().ToString("D")},CancellationToken.None);
            if(!ReferenceEquals(first.Job,replay.Job)||!replay.Replay)throw new Exception("Camera replay created a second job");
            bad=JsonSerializer.SerializeToNode(input)!;bad["display_style"]="preserve";
            Deny(()=>queue.Enqueue(request with{arguments=Wire.Element(bad)},CancellationToken.None),"OPERATION_ID_CONFLICT");
            return Task.CompletedTask;
        });
        await test("camera restoration compares projection geometry and display style",()=>{
            var view=View(bridge,document,"1") with{render_mode="Optimized2D",visual_style_handle="A"};
            if(!CameraChangeValidation.SameState(view,view with{direction_wcs=[2,-2,2]}))throw new Exception("Equivalent camera directions differ");
            foreach(var different in new[]{view with{render_mode="GouraudShadedWithWireframe"},view with{visual_style_handle="B"},view with{height=81},view with{direction_wcs=[1,1,1]},view with{center_view=[1,0]}})
                if(CameraChangeValidation.SameState(view,different))throw new Exception("Changed view accepted as restored");
            return Task.CompletedTask;
        });
        await test("camera projection accepts equivalent native target center encodings",()=>{
            var top=View(bridge,document,"1") with{direction_wcs=[0,0,1],target_wcs=[134.87436867076457,-9.25,13.5],width=371.6984848098349,height=165.37243964892653};
            var nativeTop=top with{target_wcs=[20,0,13.5],center_view=[114.87436867076457,-9.25]};
            if(!CameraChangeValidation.SameUntwistedOrthographicProjection(top,nativeTop,new(1,0,0),new(0,1,0)))throw new Exception("Native top projection rejected");
            var iso=View(bridge,document,"1") with{target_wcs=[20,0,0],center_view=[-14.142135623730951,19.187669651801563],width=136.5384484348696,height=60.747345621022816};
            var nativeIso=iso with{target_wcs=[2.1666666666666634,-2.166666666666667,15.66666666666667],center_view=[0,0],direction_wcs=[1,-1,1.0000000000000018]};
            var x=new Point3(1/Math.Sqrt(2),1/Math.Sqrt(2),0);var y=new Point3(-1/Math.Sqrt(6),1/Math.Sqrt(6),2/Math.Sqrt(6));
            if(!CameraChangeValidation.SameUntwistedOrthographicProjection(iso,nativeIso,x,y))throw new Exception("Native oblique projection rejected");
            if(CameraChangeValidation.SameState(iso,nativeIso))throw new Exception("Raw equality was incorrectly broadened");
            return Task.CompletedTask;
        });
        await test("camera projection rejects real pans clipping and unsupported basis",()=>{
            var view=View(bridge,document,"1") with{direction_wcs=[0,0,1],target_wcs=[1e9,0,0],render_mode="Optimized2D",visual_style_handle="A"};
            var same=view with{target_wcs=[1e9-20,10,500],center_view=[20,-10]};
            bool Equal(CameraView a,CameraView b)=>CameraChangeValidation.SameUntwistedOrthographicProjection(a,b,new(1,0,0),new(0,1,0));
            if(!Equal(view,same))throw new Exception("Unclipped orthographic depth shift rejected");
            foreach(var changed in new[]{same with{center_view=[20.001,-10]},same with{height=81},same with{direction_wcs=[0,0,-1]},same with{twist_radians=.1},same with{viewport_number=3},same with{visual_style_handle="B"},same with{render_mode="Other"},same with{back_clip_enabled=true}})
                if(Equal(view,changed))throw new Exception("Different projection accepted");
            if(Equal(view with{front_clip_enabled=true},same with{front_clip_enabled=true}) || Equal(view with{projection="perspective"},same with{projection="perspective"}) ||
                Equal(view with{twist_radians=.1},same with{twist_radians=.1}))throw new Exception("Unsupported projection accepted");
            foreach(var y in new[]{new Point3(0,-1,0),new Point3(1,0,0),new Point3(0,2,0),new Point3(0,double.NaN,0)})
                if(CameraChangeValidation.SameUntwistedOrthographicProjection(view,same,new(1,0,0),y))throw new Exception("Invalid basis accepted");
            return Task.CompletedTask;
        });
    }
}
