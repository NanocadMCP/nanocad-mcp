using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HostMgd.ApplicationServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Private fixed translation via direct positions. No TransformBy and no product scope change.
internal static class BlockPositionProbe
{
    internal const string CommandName="NCMCP_BLOCK_POSITION_109";
    private const string Run=@"C:\NanoCAD MCP\artifacts\core-block-position-native-109v3";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-block-position-native-109v3\block-position.dxf";
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
        string[] Ids(IEnumerable<ObjectId> ids)=>ids.Where(id=>!id.IsErased).Select(id=>id.Handle.ToString()).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var model=(BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.ModelSpace],OpenMode.ForRead);
        var references=new List<object>();
        foreach(var source in model.Cast<ObjectId>().Where(id=>!id.IsErased && !id.IsEffectivelyErased).Select(id=>new KeyValuePair<string,ObjectId>(id.Handle.ToString(),id)).OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            var b=(BlockReference)tr.GetObject(source.Value,OpenMode.ForRead,true);var attributes=new List<object>();
            foreach(ObjectId id in b.AttributeCollection)
            {
                var a=(AttributeReference)tr.GetObject(id,OpenMode.ForRead,true);
                attributes.Add(new{handle=id.Handle.ToString(),erased=id.IsErased,effectively_erased=id.IsEffectivelyErased,owner=a.OwnerId.Handle.ToString(),a.Tag,a.TextString,a.Height,a.Rotation,
                    position=new[]{a.Position.X,a.Position.Y,a.Position.Z},alignment=new[]{a.AlignmentPoint.X,a.AlignmentPoint.Y,a.AlignmentPoint.Z},horizontal=(int)a.HorizontalMode,vertical=(int)a.VerticalMode,a.WidthFactor,a.Oblique,style=a.TextStyleId.Handle.ToString(),layer=a.LayerId.Handle.ToString(),a.ColorIndex,properties=CadBlockMover.PreservedProperties(a)});
            }
            var d=(BlockTableRecord)tr.GetObject(b.BlockTableRecord,OpenMode.ForRead);
            references.Add(new{handle=source.Key,erased=source.Value.IsErased,effectively_erased=source.Value.IsEffectivelyErased,owner=b.OwnerId.Handle.ToString(),definition=d.Handle.ToString(),members=Ids(d.Cast<ObjectId>()),
                origin=new[]{d.Origin.X,d.Origin.Y,d.Origin.Z},position=new[]{b.Position.X,b.Position.Y,b.Position.Z},b.Rotation,
                scale=new[]{b.ScaleFactors.X,b.ScaleFactors.Y,b.ScaleFactors.Z},matrix=b.BlockTransform.ToArray(),properties=CadBlockMover.PreservedProperties(b),attributes});
        }
        return Wire.Element(new{block_ids=Ids(blocks.Cast<ObjectId>()),model_ids=Ids(model.Cast<ObjectId>()),references});
    }
    internal static void Execute(Descriptor? descriptor,DocumentRegistry? registry)
    {
        bool attempted=false,verified=false;
        try
        {
            Need(descriptor!=null&&registry!=null,"Missing runtime");var doc=Validate(descriptor!,registry!,true);var db=doc.Database;
            using var plan=JsonDocument.Parse(_plan!);
            var ids=plan.RootElement.GetProperty("handles").EnumerateArray().Select(x=>CadStructureWriter.Find(db,Validation.NormalizeHandle(x.GetString()!))).ToArray();
            Need(ids.Length==2&&ids.Distinct().Count()==2,"Two fixed source roots required");
            JsonElement baseline;
            using(var tr=db.TransactionManager.StartTransaction()){baseline=Snapshot(db,tr);Save("baseline.json",baseline);}
            Need(baseline.GetProperty("model_ids").EnumerateArray().Select(x=>x.GetString()).ToHashSet().SetEquals(new[]{"3C","40","44","48"}),"Expected exact four fixture roots, including opaque control48");
            var delta=new Vector3d(11,17,0);
            using(var tr=db.TransactionManager.StartTransaction())
            {
                for(int index=0;index<ids.Length;index++)
                {
                    var root=(BlockReference)tr.GetObject(ids[index],OpenMode.ForWrite);
                    var model=(BlockTableRecord)tr.GetObject(root.OwnerId,OpenMode.ForRead);
                    var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead); var expectedModel=table[BlockTableRecord.ModelSpace]; Save("root-"+index+"-guard.json",new{model.IsLayout,model.Name,expected_name=BlockTableRecord.ModelSpace,owner_is_model=model.ObjectId==expectedModel,root.IsDynamicBlock,normal=new[]{root.Normal.X,root.Normal.Y,root.Normal.Z}}); Need(model.ObjectId==expectedModel&&!root.IsDynamicBlock&&root.Normal==Vector3d.ZAxis,"Unsupported root identity or plane");
                    var attributeIds=root.AttributeCollection.Cast<ObjectId>().ToArray();var attrs=attributeIds.Select(id=>(AttributeReference)tr.GetObject(id,OpenMode.ForWrite)).ToArray();
                    Need(attrs.Length==2&&attrs.All(a=>!a.IsConstant&&!a.IsMTextAttribute&&a.HorizontalMode==TextHorizontalMode.TextLeft&&a.VerticalMode==TextVerticalMode.TextBase),"Expected two plain attributes");
                    var target=root.Position+delta;var targets=attrs.Select(a=>a.Position+delta).ToArray();
                    Log("root_position_begin",new{index});attempted=true;root.Position=target;
                    Save("root-"+index+"-after-root-set.json",Snapshot(db,tr));
                    for(int i=0;i<attrs.Length;i++)
                    {
                        // Use the captured absolute target, regardless of any native
                        // root setter side effect; never add a displacement twice.
                        var a=(AttributeReference)tr.GetObject(attributeIds[i],OpenMode.ForWrite);a.Position=targets[i];
                    }
                    Need(root.Position==target,"Root position mismatch");
                    for(int i=0;i<attrs.Length;i++)Need(((AttributeReference)tr.GetObject(attributeIds[i],OpenMode.ForRead)).Position==targets[i],"Attribute position mismatch");
                }
                Save("changed.json",Snapshot(db,tr));Log("abort_begin");tr.Abort();Log("abort_returned");
            }
            Log("transaction_disposed");
            using(var tr=db.TransactionManager.StartTransaction())
            {
                var after=Snapshot(db,tr);Save("restored.json",after);
                Need(SectionViewState.Same(baseline,after),"Raw rollback mismatch including inactive AlignmentPoint");
            }
            verified=true;Save("complete.json",new{rollback_verified=true,disposed=true,product_acceptance=false});
        }
        catch(System.Exception error){Save("failure.json",new{error=error.ToString(),attempted,outcome=attempted?(verified?"rolled_back":"unknown"):"not_started"});}
        finally{_finished=true;Pending=false;}
    }
}



