using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HostMgd.ApplicationServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Private fixed full-state undo-recording hypothesis. No product scope change or catch cleanup.
internal static class HatchUndoProbe
{
    internal const string CommandName="NCMCP_HATCH_UNDO_111";
    private const string Run=@"C:\NanoCAD MCP\artifacts\core-hatch-undo-native-111";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-hatch-undo-native-111\hatch-undo.dxf";
    private static string? _plan;
    private static bool _consumed,_finished;
    internal static bool Pending{get;private set;}
    private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Save(string name,object value)
    {
        using var stream=new FileStream(Path.Combine(Run,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        JsonSerializer.Serialize(stream,value,Wire.Json);
    }
    private static void Log(string stage,object? data=null)=>File.AppendAllText(Path.Combine(Run,"probe.jsonl"),
        JsonSerializer.Serialize(new{utc=DateTime.UtcNow.ToString("O"),stage,data},Wire.Json)+Environment.NewLine);
    private static Document Validate(Descriptor descriptor,DocumentRegistry registry,bool insideCommand)
    {
        Need(_plan!=null&&_plan.Length<8192,"Bound plan required");
        using var parsed=JsonDocument.Parse(_plan!);var p=parsed.RootElement;using var process=Process.GetCurrentProcess();
        Need(File.ReadAllText(Path.Combine(Run,_consumed?"plan.consumed.json":"plan.json"))==_plan,"Plan changed");
        Need(p.GetProperty("bridge_instance_id").GetString()==descriptor.bridge_instance_id&&
            p.GetProperty("process_id").GetInt32()==process.Id&&
            p.GetProperty("process_start_utc_ticks").GetInt64()==process.StartTime.ToUniversalTime().Ticks,"Wrong lifetime");
        var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        Need(expiry>DateTimeOffset.UtcNow&&expiry<DateTimeOffset.UtcNow.AddMinutes(10),"Expired plan");
        Need(p.GetProperty("command").GetString()==CommandName,"Wrong fixed command");
        var doc=Application.DocumentManager.MdiActiveDocument;
        Need(doc!=null&&doc.IsNamedDrawing&&!doc.IsReadOnly&&
            string.Equals(Path.GetFullPath(doc.Name),Target,StringComparison.OrdinalIgnoreCase),"Wrong owned document");
        Need(registry.Get(doc!).Id==p.GetProperty("document_id").GetString(),"Wrong document identity");
        Need(HostReadGate.IsWindowAvailable()&&(insideCommand||!HostReadGate.IsBusy(doc!)),"Host busy");
        Need((File.GetAttributes(Target)&FileAttributes.ReparsePoint)==0,"Reparse file");
        for(var parent=new DirectoryInfo(Path.GetDirectoryName(Target)!);parent!=null;parent=parent.Parent)
            Need((parent.Attributes&FileAttributes.ReparsePoint)==0,"Reparse directory");
        using var stream=File.Open(Target,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var hash=SHA256.Create();
        Need(Convert.ToHexString(hash.ComputeHash(stream))==p.GetProperty("source_sha256").GetString(),"Fixture hash changed");
        return doc!;
    }
    internal static void Pump(Descriptor descriptor,DocumentRegistry registry)
    {
        if(_finished||Pending)return;
        var path=Path.Combine(Run,"plan.json");if(!File.Exists(path))return;_plan=File.ReadAllText(path);
        try
        {
            var doc=Validate(descriptor,registry,false);
            File.Move(path,Path.Combine(Run,"plan.consumed.json"));_consumed=true;Pending=true;
            Log("scheduled_once");doc.SendStringToExecute(CommandName+"\n",false,false,false);
        }
        catch(System.Exception error)
        {
            _finished=true;Pending=false;
            Save("failure.json",new{stage="schedule_failed_no_retry",error=error.ToString(),outcome="not_started"});
        }
    }
    private static JsonElement Snapshot(Database db,Transaction tr)
    {
        var blocks=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        var model=(BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.ModelSpace],OpenMode.ForRead);
        var rows=new List<object>();
        foreach(var id in model.Cast<ObjectId>().Where(id=>!id.IsErased&&!id.IsEffectivelyErased).OrderBy(id=>id.Handle.ToString(),StringComparer.Ordinal))
        {
            var e=(Entity)tr.GetObject(id,OpenMode.ForRead);using var reactors=e.GetPersistentReactorIds();
            rows.Add(new{handle=e.Handle.ToString(),owner=e.OwnerId.Handle.ToString(),
                reactors=reactors.Cast<ObjectId>().Select(r=>new{handle=r.Handle.ToString(),erased=r.IsErased,effectively_erased=r.IsEffectivelyErased}).OrderBy(r=>r.handle,StringComparer.Ordinal).ToArray(),
                detail=EntityAdapter.Detail(e,EntityAdapter.Summary(e,model.ObjectId,"probe","probe"),CancellationToken.None)});
        }
        return Wire.Element(new{block_ids=blocks.Cast<ObjectId>().Where(id=>!id.IsErased).Select(id=>id.Handle.ToString()).OrderBy(x=>x,StringComparer.Ordinal).ToArray(),entities=rows});
    }
    internal static void Execute(Descriptor? descriptor,DocumentRegistry? registry)
    {
        bool attempted=false,verified=false;
        try
        {
            Need(descriptor!=null&&registry!=null,"Missing runtime");var doc=Validate(descriptor!,registry!,true);var db=doc.Database;
            var method=typeof(DBObject).GetMethod("AssertWriteEnabled",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(bool),typeof(bool)},null);
            Need(method!=null&&method.IsFamily,"Expected installed protected full undo-recording method");
            var boundaryId=CadStructureWriter.Find(db,"30");JsonElement baseline;
            using(var tr=db.TransactionManager.StartTransaction()){baseline=Snapshot(db,tr);Save("baseline.json",baseline);}
            Need(baseline.GetProperty("entities").EnumerateArray().Select(x=>x.GetProperty("handle").GetString()).ToHashSet().SetEquals(new[]{"2F","30","31","32","33","34","35","36"}),"Exact own eight-entity fixture required");
            using(var tr=db.TransactionManager.StartTransaction())
            {
                var boundary=(Polyline)tr.GetObject(boundaryId,OpenMode.ForWrite);
                Need(boundary.GetType()==typeof(Polyline)&&boundary.Closed&&boundary.NumberOfVertices==4&&boundary.Normal==Vector3d.ZAxis,"Expected rectangular native source");
                using(var reactors=boundary.GetPersistentReactorIds())Need(reactors.Count==1&&reactors[0].Handle.ToString()=="2F","Existing foreign association must be retained");
                Log("full_undo_record_begin",new{boundary.IsWriteEnabled,boundary.IsModified});attempted=true;
                method!.Invoke(boundary,new object[]{true,true});
                Log("full_undo_record_returned",new{boundary.IsWriteEnabled,boundary.IsModified});
                var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
                var model=(BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
                var hatch=new Hatch();hatch.SetDatabaseDefaults(db);
                model.AppendEntity(hatch);tr.AddNewlyCreatedDBObject(hatch,true);Log("hatch_added",new{handle=hatch.Handle.ToString()});
                hatch.SetHatchPattern(HatchPatternType.PreDefined,"SOLID");hatch.Associative=true;
                using(var ids=new ObjectIdCollection(new[]{boundaryId}))hatch.AppendLoop(HatchLoopTypes.External,ids);
                boundary.AddPersistentReactor(hatch.ObjectId);hatch.EvaluateHatch(true);
                using(var reactors=boundary.GetPersistentReactorIds())Need(reactors.Count==2&&reactors.Cast<ObjectId>().Contains(hatch.ObjectId),"Both original and new association expected");
                Save("changed.json",Snapshot(db,tr));Log("abort_begin");tr.Abort();Log("abort_returned");
            }
            Log("transaction_disposed");
            using(var tr=db.TransactionManager.StartTransaction())
            {
                var after=Snapshot(db,tr);Save("restored.json",after);
                Need(SectionViewState.Same(baseline,after),"Full rollback mismatch: geometry, reactor inventories or original hatch links");
            }
            verified=true;Save("complete.json",new{rollback_verified=true,disposed=true,original_association_preserved=true,product_acceptance=false});
        }
        catch(System.Exception error){Save("failure.json",new{error=error.ToString(),attempted,outcome=attempted?(verified?"rolled_back":"unknown"):"not_started"});}
        finally{_finished=true;Pending=false;}
    }
}
