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
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Private diagnostic for the official ConstraintsSamples.cs public constructor
// route now feeding a referenced extrusion. No new internal sketch network call.
internal static class DocumentDrivenModelProbe
{
    internal const string CommandName="NCMCP_DOCUMENT_DRIVEN_MODEL_PROBE";
    private const string Run=@"C:\NanoCAD MCP\artifacts\document-driven-model-native-143";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\document-driven-model-native-143\constraints.dxf";
    private static string? _plan;private static bool _finished;private static int _step;
    private static McObjectId _circle,_control,_constraint,_body,_sketch,_projection;
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
    private static void CreateLinkedModel(Document doc)
    {
        Log("linked_model_before");
        var body=new Mc3dSolid();body.DbEntity.AddToCurrentDocument();_body=body.ID;
        var sketch=body.AddPlanarSketch();sketch.Name="Document circle sketch";sketch.DbEntity.Visibility=0;McObjectManager.Add2Document(sketch.DbEntity);_sketch=sketch.ID;
        sketch.SetPlane(Plane3d.XYPlane);
        var projection=sketch.ProjectObject(_circle);Need(projection!=null,"Native source projection failed");_projection=projection.ID;
        Need(projection.SrcObjectID==_circle&&projection.HostSketchID==_sketch,"Projection is bound to a different source or sketch");
        McObjectManager.UpdateAll();
        Log("source_projection",new{projection_id=_projection.ToString(),source_id=projection.SrcObjectID.ToString(),sketch_id=projection.HostSketchID.ToString()});
        var profile=sketch.CreateProfile();profile.DbEntity.Visibility=0;McObjectManager.Add2Document(profile.DbEntity);
        Need(profile.AutoProcessExternalContours(),"Linked circle did not form a closed profile");
        using var plane=sketch.Plane;var direction=plane.SignedDistanceTo(new Point3d(0,0,1))>0?FeatureExtentDirection.Positive:FeatureExtentDirection.Negative;
        var feature=body.AddExtrudeFeature(profile.ID,20,0,direction);Need(feature!=null,"Linked extrusion creation failed");
        feature.Name="Driven cylinder";McObjectManager.UpdateAll();
        using var manager=Manager();using var info=manager.GetConstraintInfo(_constraint);
        Need(info!=null&&info.FirstObject.ObjectId==_circle,"Profile creation detached the original radial constraint");
        CheckBody(doc,10,"linked_model_created");
    }
    private static void CheckBody(Document doc,double radius,string stage)
    {
        var sketch=_sketch.GetObjectOfType<PlanarSketch>();var projection=_projection.GetObjectOfType<ProjectionSketchEnt>();Need(sketch!=null&&projection!=null&&projection.SrcObjectID==_circle&&projection.HostSketchID==_sketch,"Source projection association lost");
        using var tx=doc.Database.TransactionManager.StartTransaction();
        var model=((Db.BlockTable)tx.GetObject(doc.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        var matches=((Db.BlockTableRecord)tx.GetObject(model,Db.OpenMode.ForRead)).Cast<Db.ObjectId>().Where(id=>tx.GetObject(id,Db.OpenMode.ForRead) is Db.Solid3d&&McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==_body).ToArray();
        Need(matches.Length==1,"Native driven body identity unavailable");
        var body=(Db.Solid3d)tx.GetObject(matches[0],Db.OpenMode.ForRead);var mass=body.MassProperties;var bounds=body.GeometricExtents;double expected=Math.PI*radius*radius*20;
        Need(Math.Abs(mass.Volume-expected)<1e-7*Math.Max(1,expected),"Body volume did not follow the document constraint");
        Need(mass.Centroid.DistanceTo(new Teigha.Geometry.Point3d(0,0,10))<1e-7&&bounds.MinPoint.DistanceTo(new Teigha.Geometry.Point3d(-radius,-radius,0))<1e-7&&bounds.MaxPoint.DistanceTo(new Teigha.Geometry.Point3d(radius,radius,20))<1e-7,"Driven body placement or dimensions differ");
        var native=_body.GetObjectOfType<Mc3dSolid>();var contents=native.GetPartContents(false);Need(contents!=null&&contents.Contains(_sketch)&&contents.All(id=>id==_circle?id.GetObjectOfType<DbCircle>()?.ID==_circle:id.GetObjectOfType<HistoryItem>()?.Status.ToString()=="Ok"),"Driven history is unhealthy");
        Log(stage,new{handle=matches[0].Handle.ToString(),body_id=_body.ToString(),sketch_id=_sketch.ToString(),source_id=_circle.ToString(),radius,volume=mass.Volume,expected_volume=expected});
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
                McObjectManager.UpdateAll();Observe(10,"created");CreateLinkedModel(doc);_step=1;Log("waiting_for_visual_checkpoint");
            }
            else
            {
                File.Move(Path.Combine(Run,"continue-after-visual"),Path.Combine(Run,"continue.consumed"));
                using(var manager=Manager())
                {
                    Log("radius_update_before");Need(manager.SetConstraintParamValue(_constraint,15),"Stored radial update rejected");Need(manager.ApplyAllConstraints(),"Document radial solve failed");
                }
                McObjectManager.UpdateAll();Observe(15,"updated");
                // SDK Mc3dSolid.RebuildPart forces all components; UpdateAll alone
                // in139 left the cached sketch geometry and final solid unchanged.
                var body=_body.GetObjectOfType<Mc3dSolid>();Need(body!=null,"Driven body missing");
                Log("explicit_rebuild_before");bool rebuilt=body.RebuildPart();Log("explicit_rebuild_returned",new{rebuilt});
                Need(rebuilt,"Explicit body rebuild rejected");McObjectManager.UpdateAll();
                CheckBody(doc,15,"linked_model_updated");_finished=true;Log("complete",new{scope="document2D radius drives a referenced native extrusion; internal sketch network remains separate",rollback_tested=false});
            }
        }
        catch(Exception e){_finished=true;Log("probe_failed_no_retry",new{error=e.ToString(),outcome="Inspect recorded checkpoints; no rollback or success claim"});}
        finally{Pending=false;}
    }
}
