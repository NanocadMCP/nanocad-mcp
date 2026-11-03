using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Private independent full-solid containment sampling; no GetSection measurements.
// No source writes, appended objects, altered acceptance tolerances or replay.
internal static class LoftSurfaceProbe
{
    private const string Run=@"C:\NanoCAD MCP\artifacts\core-loft-surface-native-110";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-loft-surface-native-110\loft-surface.dxf";
    private static bool _consumed;
    internal static void Observe(string bridge,DocumentState state,Request request,Transaction tr,ObjectId modelId,Action cancel)
    {
        string plan=Path.Combine(Run,"plan.json");
        if(_consumed||request.operation!="get_document"||!File.Exists(plan)||!string.Equals(Path.GetFullPath(state.Document.Name),Target,StringComparison.OrdinalIgnoreCase))return;
        var text=File.ReadAllText(plan);if(text.Length>4096)throw new InvalidOperationException("Excessive diagnostic plan.");
        using var parsed=JsonDocument.Parse(text);var p=parsed.RootElement;using var process=Process.GetCurrentProcess();
        var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        if(p.GetProperty("bridge_instance_id").GetString()!=bridge||p.GetProperty("document_id").GetString()!=state.Id||p.GetProperty("process_id").GetInt32()!=process.Id||
            p.GetProperty("process_start_utc_ticks").GetInt64()!=process.StartTime.ToUniversalTime().Ticks||expiry<=DateTimeOffset.UtcNow||expiry>DateTimeOffset.UtcNow.AddMinutes(10))
            throw new InvalidOperationException("Diagnostic lifetime mismatch.");
        _consumed=true;File.Move(plan,plan+".consumed");
        var watch=Stopwatch.StartNew();
        void Check(){cancel();if(watch.ElapsedMilliseconds>8000)throw new InvalidOperationException("Diagnostic eight-second limit.");}
        void Save(string name,object value){using var f=new FileStream(Path.Combine(Run,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(f,value,Wire.Json);}



        double[] Point(Teigha.Geometry.Point3d q)=>new[]{q.X,q.Y,q.Z};
        void Sample(string label,Solid3d solid,Teigha.Geometry.Point3d center,double radius)
        {
            Check();using var brep=new Teigha.BoundaryRepresentation.Brep(solid);
            var observations=new List<object>();int number=0;
            foreach(double angle in new[]{0d,Math.PI/4,Math.PI/2,Math.PI})
            foreach(double offset in new[]{-.01,-1e-4,-1e-5,-1e-6,0d,1e-6,1e-5,1e-4,.01})
            {
                Check();var point=new Teigha.Geometry.Point3d(center.X+(radius+offset)*Math.Cos(angle),center.Y+(radius+offset)*Math.Sin(angle),center.Z);
                var step=new{label,number,angle,offset,point=Point(point)};
                File.AppendAllText(Path.Combine(Run,"calls.jsonl"),JsonSerializer.Serialize(new{stage="before",step},Wire.Json)+Environment.NewLine);
                using var container=brep.GetPointContainment(point,out var native);
                var row=new{angle,offset,point=Point(point),classification=native.ToString(),
                    container_is_null=container==null||container.IsNull,container_type=container?.GetType().FullName};
                observations.Add(row);number++;
                File.AppendAllText(Path.Combine(Run,"calls.jsonl"),JsonSerializer.Serialize(new{stage="returned",label,row},Wire.Json)+Environment.NewLine);
            }
            Save(label+".json",new{center=Point(center),radius,observations});
        }
        // Establish behavior on an independent exact primitive before looking
        // at the loft. Fine offsets are observations, not assumed solver accuracy.
        using(var sphere=new Solid3d())
        {
            sphere.CreateSphere(4);Sample("control-sphere",sphere,Teigha.Geometry.Point3d.Origin,4);
        }
        var handles=p.GetProperty("handles").EnumerateArray().Select(v=>Validation.NormalizeHandle(v.GetString()!)).ToArray();
        if(handles.Length!=3||handles.Distinct().Count()!=3)throw new InvalidOperationException("Three fresh source solids required.");
        Solid3d Get(int index)
        {
            var id=CadStructureWriter.Find(state.Document.Database,handles[index]);
            if(tr.GetObject(id,OpenMode.ForRead) is not Solid3d solid||solid.GetType()!=typeof(Solid3d)||solid.OwnerId!=modelId)
                throw new InvalidOperationException("Expected current own top-level solid.");
            return solid;
        }
        foreach(var sample in new[]{("piece1",0,23.5,-8d,3.5,4d),("union1",2,23.5,-8d,3.5,4d),
            ("piece2",1,29.5,-3d,11.5,3.5),("union2",2,29.5,-3d,11.5,3.5)})
            Sample(sample.Item1,Get(sample.Item2),new Teigha.Geometry.Point3d(sample.Item3,sample.Item4,sample.Item5),sample.Item6);
        Save("complete.json",new{observed=true,samples=180,elapsed_ms=watch.ElapsedMilliseconds,product_acceptance=false,
            scope="Full-solid classifications compared with exact sphere control; no acceptance tolerance changed."});
    }
}
