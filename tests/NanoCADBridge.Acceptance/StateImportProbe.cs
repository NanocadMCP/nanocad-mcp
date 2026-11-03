using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HostMgd.ApplicationServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Private one-shot fixture. No added MCP operation and no production subscriptions.
internal static class StateImportProbe
{
    internal const string CommandName="NCMCP_STATE_IMPORT_PROBE";
    private const string Run=@"C:\NanoCAD MCP\artifacts\phase6-import-native";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase6-import-native\import-target.dxf";
    private const string Source=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase6-import-native\source.dwg";
    private static string? _plan;private static bool _consumed;
    internal static bool Pending{get;private set;}
    private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Log(string stage,object? data=null)=>File.AppendAllText(Path.Combine(Run,"probe.jsonl"),JsonSerializer.Serialize(new{utc=DateTime.UtcNow.ToString("O"),stage,data},Wire.Json)+Environment.NewLine);
    private static Document Validate(Descriptor? descriptor,DocumentRegistry? registry,bool command)
    {
        Need(descriptor!=null&&registry!=null&&_plan!=null&&_plan.Length<8192,"Bound state probe required");
        Need(File.ReadAllText(Path.Combine(Run,"plan.json"))==_plan,"Plan changed");
        using var parsed=JsonDocument.Parse(_plan!);var p=parsed.RootElement;using var process=Process.GetCurrentProcess();
        Need(p.GetProperty("bridge_instance_id").GetString()==descriptor!.bridge_instance_id&&p.GetProperty("process_id").GetInt32()==process.Id&&p.GetProperty("process_start_utc_ticks").GetInt64()==process.StartTime.ToUniversalTime().Ticks,"Wrong bridge lifetime");
        var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();Need(expiry>DateTimeOffset.UtcNow&&expiry<DateTimeOffset.UtcNow.AddMinutes(10),"Expired or excessive window");
        var doc=Application.DocumentManager.MdiActiveDocument;
        Need(doc!=null&&doc.IsNamedDrawing&&!doc.IsReadOnly&&string.Equals(Path.GetFullPath(doc.Name),Target,StringComparison.OrdinalIgnoreCase),"Wrong test document");
        Need(registry!.Get(doc!).Id==p.GetProperty("document_id").GetString(),"Wrong document lifetime");
        Need(command||!HostReadGate.IsBusy(doc!),"Host busy");
        foreach(var path in new[]{Target,Source})
        {
            Need((File.GetAttributes(path)&FileAttributes.ReparsePoint)==0,"Reparse fixture");
            for(var parent=new DirectoryInfo(Path.GetDirectoryName(path)!);parent!=null;parent=parent.Parent)Need((parent.Attributes&FileAttributes.ReparsePoint)==0,"Reparse directory");
            using var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var hash=SHA256.Create();
            Need(Convert.ToHexString(hash.ComputeHash(file))==p.GetProperty(path==Target?"target_sha256":"source_sha256").GetString(),"Fixture hash changed");
        }
        return doc!;
    }
    internal static void Pump(Descriptor descriptor,DocumentRegistry registry)
    {
        if(_consumed||!File.Exists(Path.Combine(Run,"plan.json")))return;_consumed=true;
        try{_plan=File.ReadAllText(Path.Combine(Run,"plan.json"));var doc=Validate(descriptor,registry,false);Pending=true;Log("scheduled");doc.SendStringToExecute(CommandName+"\n",false,false,false);}
        catch(System.Exception e){Pending=false;Log("schedule_failed",new{error=e.ToString()});}
    }
    internal static void Execute(Descriptor? descriptor,DocumentRegistry? registry)
    {
        if(!Pending)return;
        try
        {
            var doc=Validate(descriptor,registry,true);var state=registry!.Get(doc);var db=doc.Database;
            ObjectId modelId;ObjectId[] originals;
            using(var tr=db.TransactionManager.StartTransaction())
            {
                modelId=((BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
                originals=((BlockTableRecord)tr.GetObject(modelId,OpenMode.ForRead)).Cast<ObjectId>().ToArray();
                Need(originals.Length==2&&originals.All(id=>tr.GetObject(id,OpenMode.ForRead).GetType()==typeof(Line)),"Expected two baseline lines");
            }
            File.Move(Path.Combine(Run,"plan.json"),Path.Combine(Run,"plan.consumed.json"));
            var events=new Dictionary<string,int>();void Count(string key){events[key]=events.GetValueOrDefault(key)+1;}
            void Appended(object? s,ObjectEventArgs e)=>Count("appended");void Modified(object? s,ObjectEventArgs e)=>Count("modified");
            void Erased(object? s,ObjectErasedEventArgs e)=>Count("erased");void InsertEnded(object? s,EventArgs e)=>Count("insert_ended");
            void CloneEnded(object? s,EventArgs e)=>Count("clone_ended");void XrefEnded(object? s,EventArgs e)=>Count("xref_ended");
            db.ObjectAppended+=Appended;db.ObjectModified+=Modified;db.ObjectErased+=Erased;db.InsertEnded+=InsertEnded;db.DeepCloneEnded+=CloneEnded;db.XrefSubCommandEnd+=XrefEnded;
            try
            {
                void Step(string name,Action action,bool requireAdvance)
                {
                    events.Clear();string before=state.Revision;Log("before_"+name,new{revision=before});action();string after=state.Revision;
                    bool advanced=long.Parse(after)>long.Parse(before);Log("after_"+name,new{before,after,advanced,events=events.ToArray()});
                    Need(!requireAdvance||advanced,"Existing revision did not advance for "+name);
                }
                Step("deep_clone",()=>{using var ids=new ObjectIdCollection(new[]{originals[0]});using var mapping=new IdMapping();db.DeepCloneObjects(ids,modelId,mapping,false);},true);
                ObjectId imported=ObjectId.Null;
                Step("database_insert",()=>{using var source=new Database(false,true);source.ReadDwgFile(Source,FileOpenMode.OpenForReadAndAllShare,true,null);source.CloseInput(true);imported=db.Insert("MCP_STATE_IMPORTED",source,true);},true);
                ObjectId xref=ObjectId.Null;
                Step("xref_attach",()=>xref=db.AttachXref(Source,"MCP_STATE_XREF"),true);
                Step("xref_reload_unchanged_source",()=>{using var ids=new ObjectIdCollection(new[]{xref});db.ReloadXrefs(ids);},false);
                Step("xref_detach",()=>db.DetachXref(xref),true);
                using var tr=db.TransactionManager.StartTransaction();
                var model=(BlockTableRecord)tr.GetObject(modelId,OpenMode.ForRead);var definition=(BlockTableRecord)tr.GetObject(imported,OpenMode.ForRead);
                Need(model.Cast<ObjectId>().Count()==3&&originals.All(id=>!id.IsErased),"Unexpected model contents after clone/import");
                var kinds=definition.Cast<ObjectId>().Select(id=>tr.GetObject(id,OpenMode.ForRead).GetType().Name).OrderBy(x=>x).ToArray();
                Need(kinds.SequenceEqual(new[]{"Circle","Line"}),"Imported definition differs from source");
                Log("complete",new{revision=state.Revision,model_count=3,definition_handle=imported.Handle.ToString(),definition_types=kinds,xref_detached=xref.IsErased});
            }
            finally{db.ObjectAppended-=Appended;db.ObjectModified-=Modified;db.ObjectErased-=Erased;db.InsertEnded-=InsertEnded;db.DeepCloneEnded-=CloneEnded;db.XrefSubCommandEnd-=XrefEnded;}
        }
        catch(System.Exception e){Log("probe_failed_no_retry",new{error=e.ToString()});}
        finally{Pending=false;}
    }
}
