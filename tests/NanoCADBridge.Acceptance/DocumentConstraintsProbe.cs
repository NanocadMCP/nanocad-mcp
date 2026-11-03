using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using HostMgd.ApplicationServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.DatabaseServices.StandardObjects;
using Multicad.Geometry;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Private diagnostic for the official ConstraintsSamples.cs public constructor
// route on plain2D drawing entities. Does not retry the ended sketch-scope probe.
internal static class DocumentConstraintsProbe
{
    internal const string CommandName="NCMCP_DOCUMENT_CONSTRAINTS_PROBE";
    private const string Run=@"C:\NanoCAD MCP\artifacts\document-constraints-native-138";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\document-constraints-native-138\constraints.dxf";
    private static string? _plan;private static bool _finished;private static int _step;
    private static McObjectId _circle,_control,_constraint;
    internal static bool Pending{get;private set;}
    private static void Need([DoesNotReturnIf(false)] bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Log(string stage,object? data=null)=>File.AppendAllText(Path.Combine(Run,"probe.jsonl"),JsonSerializer.Serialize(new{utc=DateTime.UtcNow.ToString("O"),step=_step,stage,data},Wire.Json)+Environment.NewLine);
    private static Document Validate(Descriptor descriptor,DocumentRegistry registry,bool inside)
    {
        Need(_plan!=null&&_plan.Length<8192,"Bound plan required");using var parsed=JsonDocument.Parse(_plan!);var p=parsed.RootElement;using var process=Process.GetCurrentProcess();
        Need(File.ReadAllText(Path.Combine(Run,_step==0?"plan.json":"plan.consumed.json"))==_plan,"Plan changed");
        Need(p.GetProperty("bridge_instance_id").GetString()==descriptor.bridge_instance_id&&p.GetProperty("process_id").GetInt32()==process.Id&&p.GetProperty("process_start_utc_ticks").GetInt64()==process.StartTime.ToUniversalTime().Ticks,"Wrong lifetime");
        var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();Need(expiry>DateTimeOffset.UtcNow&&expiry<DateTimeOffset.UtcNow.AddMinutes(10),"Expired plan");
        var doc=Application.DocumentManager.MdiActiveDocument;
        Need(doc!=null&&doc.IsNamedDrawing&&!doc.IsReadOnly&&string.Equals(Path.GetFullPath(doc.Name),Target,StringComparison.OrdinalIgnoreCase),"Wrong owned document");
        Need(registry.Get(doc!).Id==p.GetProperty("document_id").GetString(),"Wrong document identity");
        Need(HostReadGate.IsWindowAvailable()&&(inside||!HostReadGate.IsBusy(doc!)),"Host busy");
        Need((File.GetAttributes(Target)&FileAttributes.ReparsePoint)==0,"Reparse file");
        for(var dir=new DirectoryInfo(Path.GetDirectoryName(Target)!);dir!=null;dir=dir.Parent)Need((dir.Attributes&FileAttributes.ReparsePoint)==0,"Reparse directory");
        using var stream=File.Open(Target,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var hash=SHA256.Create();
        Need(Convert.ToHexString(hash.ComputeHash(stream))==p.GetProperty("source_sha256").GetString(),"Fixture hash changed");
        return doc!;
    }
    internal static void Pump(Descriptor descriptor,DocumentRegistry registry)
    {
        if(_finished||Pending||_step>0&&!File.Exists(Path.Combine(Run,"continue-after-visual")))return;
        if(_plan==null){var path=Path.Combine(Run,"plan.json");if(!File.Exists(path))return;_plan=File.ReadAllText(path);}
        try{var doc=Validate(descriptor,registry,false);Pending=true;Log("scheduled");doc.SendStringToExecute(CommandName+"\n",false,false,false);}
        catch(Exception e){_finished=true;Pending=false;Log("schedule_failed_no_retry",new{error=e.ToString()});}
    }
    private static DbCircle Circle(double x,double radius)
    {
        var circle=new DbCircle{Center=new Point3d(x,0,0),Radius=radius};circle.DbEntity.AddToCurrentDocument();circle.DbEntity.Update();return circle;
    }
    private static Constraints2D Manager()
    {
        var sheet=McDocumentsManager.GetActiveSheet();Need(sheet!=null,"Missing active sheet");
        Log("constructor_before",new{sheet=sheet!.ID.ToString()});
        var manager=new Constraints2D(Plane3d.XYPlane);
        try
        {
            Log("constructor_returned",new{document=manager.DocumentId.ToString(),scope=manager.ScopeId.ToString(),sketch=manager.PlanarSketchId.ToString(),network=manager.ConstraintsNetworkType.ToString()});
            Need(manager.DocumentId==sheet.ID&&manager.PlanarSketchId.IsNull,"Manager resolved a different document or sketch");return manager;
        }
        catch{manager.Dispose();throw;}
    }
    private static void Observe(double radius,string stage)
    {
        var a=_circle.GetObjectOfType<DbCircle>();var b=_control.GetObjectOfType<DbCircle>();
        Need(a!=null&&b!=null&&Math.Abs(a.Radius-radius)<1e-7&&Math.Abs(b.Radius-5)<1e-7,"Radial solver geometry or control differs");
        Need(a!.Center.DistanceTo(Point3d.Origin)<1e-7&&b!.Center.DistanceTo(new Point3d(50,0,0))<1e-7,"Circle center moved");
        Log(stage,new{circle_id=_circle.ToString(),control_id=_control.ToString(),constraint_id=_constraint.ToString(),radius=a.Radius,control_radius=b.Radius});
    }
    internal static void Execute(Descriptor? descriptor,DocumentRegistry? registry)
    {
        if(!Pending||_finished)return;
        try
        {
            Need(descriptor!=null&&registry!=null,"Missing runtime");var doc=Validate(descriptor!,registry!,true);
            if(_step==0)
            {
                using(var tx=doc.Database.TransactionManager.StartTransaction())
                {
                    var model=((Db.BlockTable)tx.GetObject(doc.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
                    Need(doc.Database.CurrentSpaceId==model&&!((Db.BlockTableRecord)tx.GetObject(model,Db.OpenMode.ForRead)).Cast<Db.ObjectId>().Any(),"Empty Model Space fixture required");
                }
                File.Move(Path.Combine(Run,"plan.json"),Path.Combine(Run,"plan.consumed.json"));
                _circle=Circle(0,10).ID;_control=Circle(50,5).ID;
                using(var manager=Manager())
                {
                    Log("radial_before",new{circle=_circle.ToString()});_constraint=manager.AddDimRadial(_circle);
                    Log("radial_returned",new{constraint_id=_constraint.ToString()});Need(!_constraint.IsNull,"Public document radial constructor returned Null");
                    using var info=manager.GetConstraintInfo(_constraint);Need(info!=null&&info.Type==ConstraintType.DimRadial&&info.FirstObject.ObjectId==_circle,"Radial association differs");
                    var ids=manager.GetAllConstraints();Need(ids!=null&&ids.Contains(_constraint),"Created constraint absent from inventory");
                }
                McObjectManager.UpdateAll();Observe(10,"created");_step=1;Log("waiting_for_visual_checkpoint");
            }
            else
            {
                File.Move(Path.Combine(Run,"continue-after-visual"),Path.Combine(Run,"continue.consumed"));
                using(var manager=Manager())
                {
                    Log("radius_update_before");Need(manager.SetConstraintParamValue(_constraint,15),"Stored radial update rejected");manager.ApplyAllConstraints();
                }
                McObjectManager.UpdateAll();Observe(15,"updated");_finished=true;Log("complete",new{scope="ordinary document2D radial dependency only; no internal PlanarSketch acceptance",rollback_tested=false});
            }
        }
        catch(Exception e){_finished=true;Log("probe_failed_no_retry",new{error=e.ToString(),outcome="Inspect recorded checkpoints; no rollback or success claim"});}
        finally{Pending=false;}
    }
}
