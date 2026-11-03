using System.Text.Json;
using NanoCAD.Contracts;

internal static class CameraFramingTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool valid){if(!valid)throw new Exception("Invalid camera framing.");}
        void Reject(Action work){try{work();}catch(DomainException){return;}throw new Exception("Invalid frame accepted.");}
        await test("camera framing fits asymmetric selections while preserving viewport aspect",()=>{
            var bounds=new[]{new CameraBounds(new(100,200,-5),new(120,210,5)),new CameraBounds(new(180,200,0),new(200,220,10))};
            var frame=CameraFraming.Calculate(bounds,new(1,0,0),new(0,1,0),2,.1);
            Need(frame.Target==new Point3(150,210,2.5)&&frame.Center==new Point2(0,0)&&frame.Width==120&&frame.Height==60);
            var front=CameraFraming.Calculate(bounds,new(1,0,0),new(0,0,1),.5,0);
            Need(front.Width==100&&front.Height==200);
            return Task.CompletedTask;
        });
        await test("camera framing handles degenerate display bounds and rejects invalid bases",()=>{
            var point=new[]{new CameraBounds(new(1,2,3),new(1,2,3))};var frame=CameraFraming.Calculate(point,new(1,0,0),new(0,1,0),2,0);
            Need(frame.Height==.01&&frame.Width==.02&&frame.Target==new Point3(1,2,3));
            Reject(()=>CameraFraming.Calculate(point,new(2,0,0),new(0,1,0),2,0));
            Reject(()=>CameraFraming.Calculate(new[]{new CameraBounds(new(2,0,0),new(1,0,0))},new(1,0,0),new(0,1,0),2,0));
            return Task.CompletedTask;
        });
        await test("camera framing request identity includes selected objects and padding",()=>{
            var id=Guid.NewGuid().ToString("D");var input=Wire.Element(new{bridge_instance_id=id,document_id=id,operation_id=id,expected_revision="1",expected_camera_token=new string('A',64),action="preset",preset="top",frame_handles=new[]{"0a"}});
            var args=CameraChangeValidation.Parse(input);Need(args.FrameHandles!.Single()=="A"&&args.FramePadding==.1);
            var changed=JsonSerializer.SerializeToNode(input)!;changed["frame_padding"]=.2;Need(CameraChangeValidation.Parse(Wire.Element(changed)).Fingerprint()!=args.Fingerprint());
            changed["action"]="restore";changed["restore_token"]=id;changed.AsObject().Remove("preset");Reject(()=>CameraChangeValidation.Parse(Wire.Element(changed)));
            changed=JsonSerializer.SerializeToNode(input)!;changed.AsObject().Remove("frame_handles");changed["frame_padding"]=.2;Reject(()=>CameraChangeValidation.Parse(Wire.Element(changed)));
            return Task.CompletedTask;
        });
        await test("camera framing keeps the host target with equivalent projected corner positions",()=>{
            var bounds=new[]{new CameraBounds(new(100,-30,-8),new(210,45,19))};
            double a=1/Math.Sqrt(2),b=1/Math.Sqrt(6);
            foreach(var axes in new[]{(new Point3(1,0,0),new Point3(0,1,0)),(new Point3(a,a,0),new Point3(-b,b,2*b))})
            {
                var f=CameraFraming.Calculate(bounds,axes.Item1,axes.Item2,2,.1);
                var shifted=CameraFraming.WithTarget(f,new(20,0,13.5),axes.Item1,axes.Item2);
                Need(shifted.Target==new Point3(20,0,13.5) && shifted.Width==f.Width && shifted.Height==f.Height);
                double Screen(Point3 p,CameraFrame frame,Point3 axis,double center)=>(p.X-frame.Target.X)*axis.X+(p.Y-frame.Target.Y)*axis.Y+(p.Z-frame.Target.Z)*axis.Z-center;
                foreach(var p in new[]{bounds[0].Min,bounds[0].Max,new Point3(100,45,-8)})
                    Need(Math.Abs(Screen(p,f,axes.Item1,f.Center.X)-Screen(p,shifted,axes.Item1,shifted.Center.X))<1e-10 && Math.Abs(Screen(p,f,axes.Item2,f.Center.Y)-Screen(p,shifted,axes.Item2,shifted.Center.Y))<1e-10);
            }
            return Task.CompletedTask;
        });
    }
}
