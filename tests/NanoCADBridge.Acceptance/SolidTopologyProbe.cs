using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Application=HostMgd.ApplicationServices.Application;
using Brep=Teigha.BoundaryRepresentation.Brep;
using BrepEntity=Teigha.BoundaryRepresentation.BrepEntity;
using Face=Teigha.BoundaryRepresentation.Face;
using Edge=Teigha.BoundaryRepresentation.Edge;

namespace NanoCADBridge;

// Diagnostic build only: one bound read of known owned boxes, no published tool.
internal static class SolidTopologyProbe
{
    private const string Run=@"C:\NanoCAD MCP\artifacts\core-topology-diagnostic-v2";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-topology-diagnostic-v2\topology.dwg";
    private static bool _consumed;
    private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Log(string stage,object? data=null)=>File.AppendAllText(Path.Combine(Run,"probe.jsonl"),JsonSerializer.Serialize(new{utc=DateTime.UtcNow.ToString("O"),stage,data},Wire.Json)+Environment.NewLine);
    internal static void Pump(Descriptor descriptor,DocumentRegistry registry)
    {
        var planPath=Path.Combine(Run,"plan.json");
        if(_consumed||!File.Exists(planPath))return;
        _consumed=true;
        try
        {
            var text=File.ReadAllText(planPath);Need(text.Length<8192,"Plan too large");
            using var parsed=JsonDocument.Parse(text);var plan=parsed.RootElement;
            using var process=Process.GetCurrentProcess();
            Need(plan.GetProperty("process_id").GetInt32()==process.Id&&plan.GetProperty("process_start_utc_ticks").GetInt64()==process.StartTime.ToUniversalTime().Ticks&&plan.GetProperty("bridge_instance_id").GetString()==descriptor.bridge_instance_id,"Wrong lifetime");
            var expiry=plan.GetProperty("expires_at_utc").GetDateTimeOffset();Need(expiry>DateTimeOffset.UtcNow&&expiry<DateTimeOffset.UtcNow.AddMinutes(10),"Expired plan");
            var doc=Application.DocumentManager.MdiActiveDocument;
            Need(doc!=null&&doc.IsNamedDrawing&&doc.IsReadOnly&&string.Equals(Path.GetFullPath(doc.Name),Target,StringComparison.OrdinalIgnoreCase),"Wrong owned read-only drawing");
            Need(HostReadGate.IsWindowAvailable()&&!HostReadGate.IsBusy(doc!),"Busy host");
            var state=registry.Get(doc!);Need(state.Id==plan.GetProperty("document_id").GetString(),"Wrong document lifetime");
            Need((File.GetAttributes(Target)&FileAttributes.ReparsePoint)==0,"Reparse fixture");
            for(var parent=new DirectoryInfo(Path.GetDirectoryName(Target)!);parent!=null;parent=parent.Parent)Need((parent.Attributes&FileAttributes.ReparsePoint)==0,"Reparse directory");
            using(var input=File.Open(Target,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            using(var hash=SHA256.Create())Need(Convert.ToHexString(hash.ComputeHash(input))==plan.GetProperty("source_sha256").GetString(),"Wrong fixture hash");
            File.Move(planPath,Path.Combine(Run,"plan.consumed.json"));
            var revision=state.Revision;var dirty=Application.GetSystemVariable("DBMOD");
            Log("begin",new{revision,dirty});
            try
            {
                using var tr=doc!.Database.TransactionManager.StartTransaction();
                var model=((BlockTable)tr.GetObject(doc.Database.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
                foreach(var handle in new[]{"70","73"})
                {
                    Need(doc.Database.TryGetObjectId(new Handle(Convert.ToInt64(handle,16)),out var id),"Missing box");
                    var solid=tr.GetObject(id,OpenMode.ForRead) as Solid3d;
                    Need(solid!=null&&solid.GetType()==typeof(Solid3d)&&solid.OwnerId==model,"Expected native root box");
                    Log("before_entity_brep",new{handle});
                    using var brep=new Brep(solid!);
                    Need(!brep.IsNull,"Null B-rep");
                    int faces=0,edges=0;
                    foreach(Face face in brep.Faces)
                    using(face)
                    {
                        Need(++faces<=128,"Face limit");
                        Capture(solid!,face,handle,"face",faces,()=>{using var surface=face.Surface;return new{surface_type=surface.GetType().FullName};});
                    }
                    foreach(Edge edge in brep.Edges)
                    using(edge)
                    {
                        Need(++edges<=256,"Edge limit");
                        Capture(solid!,edge,handle,"edge",edges,()=>
                        {
                            using var a=edge.Vertex1;using var b=edge.Vertex2;
                            return new{start=new[]{a.Point.X,a.Point.Y,a.Point.Z},end=new[]{b.Point.X,b.Point.Y,b.Point.Z}};
                        });
                    }
                    Log("body_complete",new{handle,faces,edges});
                }
            }
            finally{Log("state_after",new{revision=state.Revision,dirty=Application.GetSystemVariable("DBMOD"),revision_unchanged=state.Revision==revision,dirty_unchanged=Equals(dirty,Application.GetSystemVariable("DBMOD"))});}
            Log("complete");
        }
        catch(System.Exception error){Log("probe_failed_no_retry",new{error=error.ToString()});}
    }
    private static void Capture(Solid3d solid,BrepEntity entity,string handle,string kind,int ordinal,Func<object> geometry)
    {
        Log("before_path",new{handle,kind,ordinal});
        var path=entity.SubentityPath;var parents=path.GetObjectIds();var subent=path.SubentId;
        Log("path",new{handle,kind,ordinal,path_is_null=path.IsNull,parent_count=parents.Length,
            parents=parents.Select(id=>new{is_null=id.IsNull,handle=id.IsNull?null:id.Handle.ToString()}).ToArray(),
            subentity_type=subent.Type.ToString(),native_id=subent.IndexPtr.ToInt64().ToString()});
        try{Log("geometry",new{handle,kind,ordinal,value=geometry()});}
        catch(System.Exception error){Log("geometry_failed",new{handle,kind,ordinal,error=error.ToString()});}
        // Resolve the reported identifier through the known source body, copying
        // geometry to an owned transient object only; never append or mutate it.
        Log("before_copy_subentity",new{handle,kind,ordinal});
        try
        {
            using var copy=kind=="face"?solid.CopyFace(subent):solid.CopyEdge(subent);
            Need(copy!=null&&copy.ObjectId.IsNull,"Copy must be transient");
            var bounds=copy!.GeometricExtents;
            Log("copy_subentity",new{handle,kind,ordinal,type=copy.GetType().FullName,
                min=new[]{bounds.MinPoint.X,bounds.MinPoint.Y,bounds.MinPoint.Z},max=new[]{bounds.MaxPoint.X,bounds.MaxPoint.Y,bounds.MaxPoint.Z}});
        }
        catch(System.Exception error){Log("copy_subentity_failed",new{handle,kind,ordinal,error=error.ToString()});}
    }
}
