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

// Private fixed fixture: create, commit, and rollback in separate native commands.
internal static class CoreParameterProbe
{
    internal const string CommandName="NCMCP_CORE_PARAMETER_PROBE";
    private const string Run=@"C:\NanoCAD MCP\artifacts\core-parameter-rollback-native";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-parameter-rollback-native\parameter.dxf";
    private static string? _plan,_handle;private static int _step;private static bool _finished;
    private static McObjectId _body,_variable;
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
        if(_plan==null)
        {
            var path=Path.Combine(Run,"plan.json");if(!File.Exists(path))return;_plan=File.ReadAllText(path);
        }
        try{var doc=Validate(descriptor,registry,false);Pending=true;Log("scheduled");doc.SendStringToExecute(CommandName+"\n",false,false,false);}
        catch(System.Exception error){_finished=true;Pending=false;Log("schedule_failed_no_retry",new{error=error.ToString()});}
    }
    private static JsonElement Snapshot(Descriptor descriptor,DocumentState state)
    {
        var args=new ModelParameterArguments(descriptor.bridge_instance_id,state.Id,state.Revision,Guid.NewGuid().ToString("D"),"Millimeters",_handle!,_variable.ToString(),"100",120);
        var snapshot=ModelParameterSnapshot.Capture(args,state);ModelParameterSnapshot.Healthy(snapshot);return snapshot;
    }
    private static void Set(string expression)
    {
        var body=_body.GetObjectOfType<Mc3dSolid>();Need(body!=null&&body.ID==_body,"Body identity changed");
        using(var manager=body!.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS))
            Need(manager!=null&&manager.SetVariableExpression(_variable,expression),"Expression rejected");
        McObjectManager.UpdateAll();
    }
    internal static void Execute(Descriptor? descriptor,DocumentRegistry? registry)
    {
        if(!Pending||_finished)return;
        try
        {
            Need(descriptor!=null&&registry!=null,"Missing runtime");var doc=Validate(descriptor!,registry!,true);var state=registry!.Get(doc);
            if(_step==0)
            {
                using(var tx=doc.Database.TransactionManager.StartTransaction())
                {
                    var model=((BlockTable)tx.GetObject(doc.Database.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
                    Need(!((BlockTableRecord)tx.GetObject(model,OpenMode.ForRead)).Cast<ObjectId>().Any(),"Fixture must begin empty");
                }
                File.Move(Path.Combine(Run,"plan.json"),Path.Combine(Run,"plan.consumed.json"));
                Log("create_before");
                var solid=new Mc3dSolid();solid.DbEntity.AddToCurrentDocument();
                var sketch=solid.AddPlanarSketch();sketch.DbEntity.Visibility=0;McObjectManager.Add2Document(sketch.DbEntity);
                sketch.AddGeometry(McObjectId.NewID(),new CircArc3d(Point3d.Origin,Vector3d.ZAxis,10));
                var profile=sketch.CreateProfile();profile.DbEntity.Visibility=0;McObjectManager.Add2Document(profile.DbEntity);profile.AutoProcessExternalContours();
                var extrusion=solid.AddExtrudeFeature(profile.ID,100,0,FeatureExtentDirection.Negative);McObjectManager.UpdateAll();
                _body=solid.ID;_variable=extrusion.DistanceParamVarID;
                using(var tx=doc.Database.TransactionManager.StartTransaction())
                {
                    var model=((BlockTable)tx.GetObject(doc.Database.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
                    var matches=((BlockTableRecord)tx.GetObject(model,OpenMode.ForRead)).Cast<ObjectId>().Where(id=>tx.GetObject(id,OpenMode.ForRead) is Solid3d&&McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==_body).ToArray();
                    Need(matches.Length==1,"Native body identity unavailable");_handle=matches[0].Handle.ToString();
                }
                var initial=Snapshot(descriptor!,state);Log("created",new{handle=_handle,variable_id=_variable.ToString(),snapshot=initial});
                Need(ModelParameterSnapshot.Near(initial.GetProperty("native").GetProperty("volume").GetDouble(),10000*Math.PI),"Initial volume oracle differs");
                _step=1;
            }
            else if(_step==1)
            {
                using(var tracker=new McUndoPoint(false)){tracker.Start();Set("120");tracker.Stop();}
                var committed=Snapshot(descriptor!,state);Log("positive_committed",committed);
                Need(ModelParameterSnapshot.Near(committed.GetProperty("native").GetProperty("volume").GetDouble(),12000*Math.PI),"Positive volume oracle differs");
                _step=2;
            }
            else
            {
                var baseline=Snapshot(descriptor!,state);Log("rollback_baseline",baseline);
                using(var tracker=new McUndoPoint(false))
                {
                    tracker.Start();
                    try{Set("140");Log("intentional_failure_after_rebuild",Snapshot(descriptor!,state));}
                    finally{tracker.Undo(false);tracker.Stop();}
                }
                McObjectManager.UpdateAll();var restored=Snapshot(descriptor!,state);Log("rollback_restored",restored);
                Need(ModelParameterSnapshot.Equivalent(baseline,restored),"Rollback differs after full restore and deferred rebuild");
                _finished=true;Log("complete",new{handle=_handle,variable_id=_variable.ToString(),undo_update=false,command_boundaries=3,final_distance=120});
            }
        }
        catch(System.Exception error){_finished=true;Log("probe_failed_no_retry",new{error=error.ToString(),outcome="Inspect exact completed stages; no inferred rollback"});}
        finally{Pending=false;}
    }
}
