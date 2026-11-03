using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HostMgd.ApplicationServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Application=HostMgd.ApplicationServices.Application;
namespace NanoCADBridge;
// Private fixed fixture: create and change own sketches in separate native commands; no forced rollback.
internal static class ParametricSketchProbe
{
    internal const string CommandName="NCMCP_PARAMETRIC_SKETCH_PROBE";
    private const string Run=@"C:\NanoCAD MCP\artifacts\parametric-sketch-native-328";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\parametric-sketch-native-328\sketch.dwg";
    private static string? _plan;private static int _step=0;private static bool _finished;
    internal static bool Pending{get;private set;}
    private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Log(string stage,object? data=null)=>File.AppendAllText(Path.Combine(Run,"probe.jsonl"),JsonSerializer.Serialize(new{utc=DateTime.UtcNow.ToString("O"),step=_step,stage,data},Wire.Json)+Environment.NewLine);
    private static Document Validate(Descriptor descriptor,DocumentRegistry registry,bool insideCommand)
    {
        Need(_plan!=null&&_plan.Length<8192,"Bound plan required");
        using var parsed=JsonDocument.Parse(_plan!);var p=parsed.RootElement;using var process=Process.GetCurrentProcess();
        var path=Path.Combine(Run,_step==0?"plan.json":"plan.consumed.json");Need(File.ReadAllText(path)==_plan,"Plan changed");
        Need(p.GetProperty("bridge_instance_id").GetString()==descriptor.bridge_instance_id&&p.GetProperty("process_id").GetInt32()==process.Id&&p.GetProperty("process_start_utc_ticks").GetInt64()==process.StartTime.ToUniversalTime().Ticks,"Wrong lifetime");
        var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();Need(expiry>DateTimeOffset.UtcNow&&expiry<DateTimeOffset.UtcNow.AddMinutes(10),"Expired plan");
        var doc=Application.DocumentManager.MdiActiveDocument;
        Need(doc!=null&&doc.IsNamedDrawing&&!doc.IsReadOnly&&string.Equals(Path.GetFullPath(doc.Name),Target,StringComparison.OrdinalIgnoreCase),"Wrong owned document");
        Need(registry.Get(doc!).Id==p.GetProperty("document_id").GetString(),"Wrong document identity");
        Need(HostReadGate.IsWindowAvailable()&&(insideCommand||!HostReadGate.IsBusy(doc!)),"Host busy");
        Need((File.GetAttributes(Target)&FileAttributes.ReparsePoint)==0,"Reparse file");
        for(var parent=new DirectoryInfo(Path.GetDirectoryName(Target)!);parent!=null;parent=parent.Parent)Need((parent.Attributes&FileAttributes.ReparsePoint)==0,"Reparse directory");
        using var stream=File.Open(Target,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var hash=SHA256.Create();
        Need(Convert.ToHexString(hash.ComputeHash(stream))==p.GetProperty("source_sha256").GetString(),"Fixture hash changed");
        return doc!;
    }
    internal static void Pump(Descriptor descriptor,DocumentRegistry registry)
    {
        if(_finished||Pending)return;
        if(_step>0&&!File.Exists(Path.Combine(Run,"continue-after-visual")))return;
        if(_plan==null)
        {
            var path=Path.Combine(Run,"plan.json");if(!File.Exists(path))return;_plan=File.ReadAllText(path);
        }
        try{var doc=Validate(descriptor,registry,false);Pending=true;Log("scheduled");doc.SendStringToExecute(CommandName+"\n",false,false,false);}
        catch(System.Exception error){_finished=true;Pending=false;Log("schedule_failed_no_retry",new{error=error.ToString()});}
    }
    private sealed record Part(McObjectId Body, McObjectId Sketch, McObjectId Circle, McObjectId Constraint, McObjectId Variable, double Radius, double Height);
    private static Part? _first, _control;
    private static string? Id(McObjectId id) => id.IsNull ? null : id.ToString();
    private static void Scope(string stage, McDocument owner, PlanarSketch sketch)
    {
        using var manager=owner.Get2dConstraintsManager(Constraint2dNetType.Auto,sketch.ID,sketch.Plane);
        Need(manager!=null,"No Auto sketch manager");
        Need(!sketch.IsInEditMode&&manager!.PlanarSketchId==sketch.ID,"Auto manager did not resolve the requested outside-edit sketch");
        var ids=manager!.GetAllConstraints();
        if(ids==null){Log(stage,new{requested=Id(sketch.ID),owner=Id(owner.ID),document=Id(manager.DocumentId),scope=Id(manager.ScopeId),sketch=Id(manager.PlanarSketchId),inventory="unavailable; not interpreted as empty"});return;}
        Need(ids.Count<=32,"Unexpected constraint inventory");
        var details=new List<object>();
        foreach(var id in ids!)
        {
            using var info=manager.GetConstraintInfo(id);
            Need(info!=null,"Missing constraint details");
            details.Add(new{id=Id(id),type=info!.Type.ToString(),first=info.FirstObject==null?null:Id(info.FirstObject.ObjectId),variable=Id(info.VarId),name=info.VarName,expression=info.VarExpression});
        }
        Log(stage,new{requested=Id(sketch.ID),owner=Id(owner.ID),document=Id(manager.DocumentId),scope=Id(manager.ScopeId),sketch=Id(manager.PlanarSketchId),network=manager.ConstraintsNetworkType.ToString(),constraints=details});
    }
    private static double ReadRadius(PlanarSketch sketch,McObjectId id)
    {
        var geometry=sketch.GetAllItemsGeometry(false);Need(geometry!=null&&geometry.Count==1,"Expected exactly one internal sketch element");
        try
        {
            Need(geometry!.TryGetValue(id,out var value)&&value!=null&&value.GeometryType==EntityGeomType.kCircArc,"Internal circle identity unavailable");
            using var circle=value!.CircArc;return circle.Radius;
        }
        finally {if(geometry!=null)foreach(var value in geometry.Values)value?.Dispose();}
    }
    private static Part Create(string name,double x,double radius,double height,bool constrain=true)
    {
        Log("create_begin",new{name,route="public_current_sheet_constructor_and_verified_element_identity"});
        var solid=new Mc3dSolid();solid.DbEntity.AddToCurrentDocument();
        var sketch=solid.AddPlanarSketch();sketch.Name=name+" Sketch";sketch.DbEntity.Visibility=0;McObjectManager.Add2Document(sketch.DbEntity);
        // Match the accepted product factory: initialize the plane explicitly
        // before entering the native editor. AddGeometry had hidden this omission.
        using(var initialPlane=new Plane3d(Multicad.Geometry.Point3d.Origin,Multicad.Geometry.Vector3d.XAxis,Multicad.Geometry.Vector3d.YAxis))sketch.SetPlane(initialPlane);
        sketch.DbEntity.Update();
        McObjectId constraintId=McObjectId.Null,variableId=McObjectId.Null,circleId=McObjectId.Null;
        Need(sketch.EnterEditMode(),"Sketch edit mode unavailable");
        try {
            var circle=new Multicad.DatabaseServices.StandardObjects.DbCircle{Center=new Multicad.Geometry.Point3d(x,0,0),Radius=radius};
            circle.DbEntity.AddToCurrentDocument();var attached=sketch.AddObject(circle.ID);circleId=circle.ID;sketch.DbEntity.Update();
            var included=sketch.GetAllItemsGeometry(false);
            try {Log("added_object_identity",new{attached,source=Id(circle.ID),owner=Id(circle.DbEntity.Document.ID),elements=included?.Keys.Select(Id).ToArray()});}
            finally {if(included!=null)foreach(var item in included.Values)item?.Dispose();}
            Need(attached,"Native AddObject did not attach the source circle");
            if(constrain){
            using var plane=sketch.Plane;
            using var manager=new Constraints2D(plane);
            Need(manager!=null,"SDK sample MCS manager unavailable");
            Log("edit_scope",new{sketch=Id(sketch.ID),actual_sketch=Id(manager!.PlanarSketchId),scope=Id(manager.ScopeId),document=Id(manager.DocumentId),element=Id(circleId),network=manager.ConstraintsNetworkType.ToString()});
            // The documented edit-mode sample uses an MCS manager whose sketch/scope
            // metadata can be null. This isolated empty fixture tests that exact
            // route; product ownership checks are not relaxed on this evidence.
            Need(manager.PlanarSketchId.IsNull||manager.PlanarSketchId==sketch.ID,"Manager identifies another sketch");
            using var info=new ConstraintInfo(ConstraintType.DimRadial,circleId){Param=radius};
            constraintId=manager.AddDimRadial(circleId);Log("constraint_returned",new{id=Id(constraintId)});
            Need(!constraintId.IsNull,"SDK sample AddObject radial constraint rejected");
            Need(manager.SetConstraintParamValue(constraintId,radius),"Initial radius assignment rejected");
            variableId=manager.GetConstraintParamId(constraintId);
            Need(manager.ApplyAllConstraints(),"SDK sample solve rejected");
            using var retained=manager.GetConstraintInfo(constraintId);
            Log("relation_before_exit",new{id=Id(constraintId),value=retained?.Param,variable=Id(variableId),sketch_status=sketch.Status.ToString()});
            }
        } finally {var exited=sketch.ExitEditMode();Log("exit_edit",new{exited,editing=sketch.IsInEditMode,status=sketch.Status.ToString()});Need(exited&&!sketch.IsInEditMode,"Native sketch edit did not exit successfully");}
        sketch.DbEntity.Update();McObjectManager.UpdateAll();
        Log("sketch_rebuilt_before_profile",new{name,status=sketch.Status.ToString()});
        var profile=sketch.CreateProfile();profile.DbEntity.Visibility=0;McObjectManager.Add2Document(profile.DbEntity);
        Need(profile.AutoProcessExternalContours(),"Native circular profile unavailable");
        var feature=solid.AddExtrudeFeature(profile.ID,height,0,FeatureExtentDirection.Positive);Need(feature!=null,"Extrusion unavailable");
        McObjectManager.UpdateAll();
        var geometry=sketch.GetAllItemsGeometry(false);Need(geometry!=null&&geometry.Count==1,"Expected single circle");
        try {circleId=geometry!.Single().Key;} finally {if(geometry!=null)foreach(var item in geometry.Values)item?.Dispose();}
        Scope(name+"_outside_edit",solid.DbEntity.Document,sketch);
        var part=new Part(solid.ID,sketch.ID,circleId,constraintId,variableId,radius,height);Log("part_created",new{body=Id(part.Body),sketch=Id(part.Sketch),circle=Id(part.Circle),constraint=Id(part.Constraint),variable=Id(part.Variable),radius,height,sketch_status=sketch.Status.ToString(),profile_status=profile.Status.ToString(),feature_status=feature!.Status.ToString()});return part;
    }
    private static void CheckPart(Document doc,Part part,double radius,string stage)
    {
        var sketch=part.Sketch.GetObjectOfType<PlanarSketch>();Need(sketch!=null,"Sketch missing");var observedRadius=ReadRadius(sketch!,part.Circle);
        Need(Math.Abs(observedRadius-radius)<1e-7,"Native sketch radius differs");
        using var tx=doc.Database.TransactionManager.StartTransaction();
        var model=((BlockTable)tx.GetObject(doc.Database.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
        var matches=((BlockTableRecord)tx.GetObject(model,OpenMode.ForRead)).Cast<ObjectId>().Where(id=>tx.GetObject(id,OpenMode.ForRead) is Solid3d&&McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==part.Body).ToArray();
        Need(matches.Length==1,"Native body identity unavailable");
        var body=(Solid3d)tx.GetObject(matches[0],OpenMode.ForRead);var volume=body.MassProperties.Volume;
        double expected=Math.PI*radius*radius*part.Height;
        Need(Math.Abs(volume-expected)<=1e-7*Math.Max(1,expected),"Extrusion did not follow sketch radius");
        var history=part.Body.GetObjectOfType<Mc3dSolid>().GetPartContents(false);
        Need(history!=null&&history.All(id=>id.GetObjectOfType<HistoryItem>()?.Status.ToString()=="Ok"),"Native history is not healthy");
        Need(history!=null&&history.Contains(part.Sketch),"Sketch detached from history");
        Log(stage,new{handle=matches[0].Handle.ToString(),radius=observedRadius,volume,expected_volume=expected,sketch_id=Id(part.Sketch),constraint_id=Id(part.Constraint),variable_id=Id(part.Variable),history=history!.Select(Id).ToArray()});
    }
    private static Part Load(Document doc,string handle,double radius,double height)
    {
        var bodyId=McObjectId.FromHandle(Convert.ToInt64(handle,16),McDocument.ActiveDocument.ID);
        var body=bodyId.GetObjectOfType<Mc3dSolid>();Need(body!=null,"Saved native body missing");
        var sketch=body!.GetPartContents(false).Select(id=>id.GetObjectOfType<PlanarSketch>()).Single(x=>x!=null)!;
        var geometry=sketch.GetAllItemsGeometry(false);Need(geometry!=null&&geometry.Count==1,"Saved circle missing");
        McObjectId circle;try{circle=geometry!.Single().Key;}finally{if(geometry!=null)foreach(var item in geometry.Values)item?.Dispose();}
        var part=new Part(bodyId,sketch.ID,circle,McObjectId.Null,McObjectId.Null,radius,height);
        CheckPart(doc,part,radius,"saved_baseline");return part;
    }
    internal static void Execute(Descriptor? descriptor,DocumentRegistry? registry)
    {
        if(!Pending||_finished)return;
        try {
            Need(descriptor!=null&&registry!=null,"Missing runtime");var doc=Validate(descriptor!,registry!,true);
            File.Move(Path.Combine(Run,"plan.json"),Path.Combine(Run,"plan.consumed.json"));
            _control=Load(doc,"6E",5,7);_first=Load(doc,"88",10,20);
            var sketch=_first.Sketch.GetObjectOfType<PlanarSketch>()!;var body=_first.Body.GetObjectOfType<Mc3dSolid>();
            using(var plane=sketch.Plane)
            using(var manager=body.DbEntity.Document.Get2dConstraintsManager(Constraint2dNetType.Auto,sketch.ID,plane)) {
                Need(manager!=null&&manager.PlanarSketchId==sketch.ID&&manager.ScopeId==sketch.ID,"Wrong internal sketch scope");
                var existing=manager!.GetAllConstraints();Need(existing!=null,"Internal network was not initialized");
                Log("internal_scope_before",new{network=manager.ConstraintsNetworkType.ToString(),sketch=Id(sketch.ID),element=Id(_first.Circle),existing=existing!.Select(Id).ToArray()});
                using var info=new ConstraintInfo(ConstraintType.DimRadial,_first.Circle){Param=10};
                var id=manager.AddConstraint(info,true);Log("internal_radial_added",new{id=Id(id)});Need(!id.IsNull,"Initialized internal network rejected radius");
                _first=_first with{Constraint=id,Variable=manager.GetConstraintParamId(id)};
                using var retained=manager.GetConstraintInfo(id);Need(retained!=null&&retained.FirstObject.ObjectId==_first.Circle,"Internal relation identity differs");
                Need(manager.SetConstraintParamValue(id,15),"Internal driving radius change rejected");
            }
            Need(sketch.ApplyConstraints(true),"Internal solve rejected");sketch.DbEntity.Update();McObjectManager.UpdateAll();
            Need(body.RebuildPart(),"Dependent body rebuild rejected");McObjectManager.UpdateAll();
            CheckPart(doc,_first,15,"driven_radius_and_body");CheckPart(doc,_control,5,"unchanged_control");
            Scope("retained_internal_constraints",body.DbEntity.Document,sketch);
            Log("complete",new{scope="initialized internal radial dependency",rollback_tested=false});
        } catch(System.Exception error){Log("probe_failed_no_retry",new{error=error.ToString(),outcome="Read checkpoints; no rollback claim"});}
        finally{_finished=true;Pending=false;}
    }
}

