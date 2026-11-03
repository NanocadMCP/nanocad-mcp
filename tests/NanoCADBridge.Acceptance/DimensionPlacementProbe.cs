using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HostMgd.ApplicationServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Private fixed three-mode text-placement observation; no product capability change.
internal static class DimensionPlacementProbe
{
    internal const string CommandName="NCMCP_DIMENSION_PLACEMENT_107";
    private const string Run=@"C:\NanoCAD MCP\artifacts\core-dimension-placement-native-107";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-dimension-placement-native-107\dimension-placement.dxf";
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
    private static double[] P(Point3d p)=>new[]{p.X,p.Y,p.Z};
    private static JsonElement Capture(Database db,Transaction tr,ObjectId[] dimensions)
    {
        var blocks=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        var model=(BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.ModelSpace],OpenMode.ForRead);
        var rows=new List<object>();
        foreach(var id in dimensions)
        {
            var d=(DiametricDimension)tr.GetObject(id,OpenMode.ForRead);
            var block=(BlockTableRecord)tr.GetObject(d.DimBlockId,OpenMode.ForRead);
            var graphics=new List<object>();
            foreach(ObjectId child in block)
            {
                if(child.IsErased)continue;
                Need(graphics.Count<128,"Unexpected dimension graphics size");
                var entity=(Entity)tr.GetObject(child,OpenMode.ForRead);var bounds=entity.GeometricExtents;
                graphics.Add(new{handle=child.Handle.ToString(),type=entity.GetType().FullName,
                    min=P(bounds.MinPoint),max=P(bounds.MaxPoint),layer=entity.LayerId.Handle.ToString(),color=entity.ColorIndex,
                    text=entity is MText m?m.Contents:entity is DBText t?t.TextString:null,
                    text_location=entity is MText m2?P(m2.Location):entity is DBText t2?P(t2.Position):null,
                    attachment=entity is MText m3?m3.Attachment.ToString():null});
            }
            rows.Add(new{handle=id.Handle.ToString(),definition=DimensionGeometryReader.Definition(d),text=d.DimensionText,
                dimtmove=d.Dimtmove,dimtad=d.Dimtad,dimtoh=d.Dimtoh,dimtih=d.Dimtih,dimatfit=d.Dimatfit,
                text_position=P(d.TextPosition),default_position=d.UsingDefaultTextPosition,rotation=d.TextRotation,
                style=d.DimensionStyle.Handle.ToString(),block=d.DimBlockId.Handle.ToString(),graphics});
        }
        return Wire.Element(new{model_ids=model.Cast<ObjectId>().Select(id=>id.Handle.ToString()).OrderBy(x=>x).ToArray(),
            block_ids=blocks.Cast<ObjectId>().Select(id=>id.Handle.ToString()).OrderBy(x=>x).ToArray(),dimensions=rows});
    }
    internal static void Execute(Descriptor? descriptor,DocumentRegistry? registry)
    {
        bool mutationAttempted=false,rollbackVerified=false;
        try
        {
            Need(descriptor!=null&&registry!=null,"Missing runtime");
            var doc=Validate(descriptor!,registry!,true);var db=doc.Database;
            using var parsed=JsonDocument.Parse(_plan!);
            var handles=parsed.RootElement.GetProperty("handles").EnumerateArray().Select(x=>x.GetString()!).ToArray();
            Need(handles.Length==3&&handles.Distinct().Count()==3,"Exactly three fresh dimensions required");
            var ids=handles.Select(h=>CadStructureWriter.Find(db,Validation.NormalizeHandle(h))).ToArray();
            JsonElement baseline;
            using(var read=db.TransactionManager.StartTransaction())
            {
                for(int mode=0;mode<3;mode++)
                {
                    Need(read.GetObject(ids[mode],OpenMode.ForRead) is DiametricDimension,"Unexpected type");
                    var d=(DiametricDimension)read.GetObject(ids[mode],OpenMode.ForRead);
                    Need(d.DimensionText=="<>"&&d.Dimtmove==mode,"Unexpected source label/movement policy");
                    var style=(DimStyleTableRecord)read.GetObject(d.DimensionStyle,OpenMode.ForRead);
                    Need(style.Name=="MCP_DIMMOVE_107_"+mode,"Only fixed diagnostic styles allowed");
                }
                baseline=Capture(db,read,ids);Save("baseline.json",baseline);
            }
            var watch=Stopwatch.StartNew();
            for(int mode=0;mode<3;mode++)
            {
                Need(watch.ElapsedMilliseconds<15000,"Diagnostic time budget exceeded");
                rollbackVerified=false;
                using(var tr=db.TransactionManager.StartTransaction())
                {
                    var d=(DiametricDimension)tr.GetObject(ids[mode],OpenMode.ForWrite);
                    Log("text_change_begin",new{mode});mutationAttempted=true;
                    d.DimensionText="Diameter <> mm";d.RecomputeDimensionBlock(true);
                    Save("mode-"+mode+"-changed.json",Capture(db,tr,ids));
                    Log("abort_begin",new{mode});tr.Abort();Log("abort_returned",new{mode});
                }
                Log("transaction_disposed",new{mode});
                using(var tr=db.TransactionManager.StartTransaction())
                {
                    var after=Capture(db,tr,ids);Save("mode-"+mode+"-restored.json",after);
                    Need(SectionViewState.Same(baseline,after),"Complete dimension/graphics rollback mismatch");
                }
                rollbackVerified=true;Save("mode-"+mode+"-rollback.json",new{verified=true});
            }
            Save("complete.json",new{observed_modes=3,rollback_verified=rollbackVerified,disposed=true,product_acceptance=false});
        }
        catch(System.Exception error)
        {
            Save("failure.json",new{error=error.ToString(),mutation_attempted=mutationAttempted,
                outcome=mutationAttempted?(rollbackVerified?"rolled_back":"unknown"):"not_started"});
        }
        finally{_finished=true;Pending=false;}
    }
}
