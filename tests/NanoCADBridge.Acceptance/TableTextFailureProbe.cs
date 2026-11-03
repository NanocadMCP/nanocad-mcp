using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// One operation in one owned drawing; absent from ordinary assemblies.
internal sealed class TableTextFailureProbe
{
    private const string Run=@"C:\NanoCAD MCP\artifacts\phase4b-table-size-native";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase4b-table-size-native\table-size.dxf";
    private static bool _consumed;
    private readonly DocumentState _state;
    private readonly CadModifier.Resolved _resolved;
    private readonly JsonElement _before;
    private TableTextFailureProbe(DocumentState state,CadModifier.Resolved resolved,Transaction tr)
    { _state=state;_resolved=resolved;_before=Snapshot(tr);Save("rollback-before.json",_before); }
    private static void Save(string name,object value)
    {using var file=new FileStream(Path.Combine(Run,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(file,value,Wire.Json);}
    public static TableTextFailureProbe? Begin(ModifyArguments args,DocumentState state,CadModifier.Resolved resolved,Transaction tr)
    {
        var plan=Path.Combine(Run,"failure-plan.json");
        if(_consumed || !File.Exists(plan) || !string.Equals(Path.GetFullPath(state.Document.Name),Target,StringComparison.OrdinalIgnoreCase))return null;
        var text=File.ReadAllText(plan);if(text.Length>16384)throw new InvalidOperationException("Excessive failure plan.");
        using var parsed=JsonDocument.Parse(text);var p=parsed.RootElement;
        if(p.GetProperty("operation_id").GetString()!=args.OperationId)return null;
        using var process=Process.GetCurrentProcess();var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        if(p.GetProperty("bridge_instance_id").GetString()!=args.BridgeId || p.GetProperty("document_id").GetString()!=args.DocumentId ||
            p.GetProperty("process_id").GetInt32()!=process.Id || p.GetProperty("process_start_utc_ticks").GetInt64()!=process.StartTime.ToUniversalTime().Ticks ||
            expiry<=DateTimeOffset.UtcNow || expiry>DateTimeOffset.UtcNow.AddMinutes(10) || !args.Operations.All(op=>op is ReplaceTableTextSpec or ResizeTableSpec) || ModifyValidation.Parse(p.GetProperty("request")).Fingerprint()!=args.Fingerprint())
            throw new InvalidOperationException("Failure plan lifetime/scope mismatch.");
        _consumed=true;File.Move(plan,plan+".consumed");return new(state,resolved,tr);
    }
    private JsonElement Snapshot(Transaction tr)
    {
        var db=_state.Document.Database;var blocks=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        var blockIds=blocks.Cast<ObjectId>().Select(id=>id.Handle.ToString()).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var model=(BlockTableRecord)tr.GetObject(_resolved.Model,OpenMode.ForRead);
        var modelIds=model.Cast<ObjectId>().Select(id=>id.Handle.ToString()).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var tables=new List<object>();
        foreach(var source in _resolved.Sources.OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            var table=(Table)tr.GetObject(source.Value,OpenMode.ForRead);var block=(BlockTableRecord)tr.GetObject(table.BlockTableRecord,OpenMode.ForRead);
            var graphics=new List<object>();
            foreach(ObjectId id in block)
            {
                if(graphics.Count>=2000)throw new InvalidOperationException("Table graphics audit exceeded2000 entities.");
                var entity=(Entity)tr.GetObject(id,OpenMode.ForRead);var bounds=entity.GeometricExtents;
                graphics.Add(new{handle=id.Handle.ToString(),type=entity.GetType().FullName,layer=entity.LayerId.Handle.ToString(),color=entity.ColorIndex,
                    minimum=new[]{bounds.MinPoint.X,bounds.MinPoint.Y,bounds.MinPoint.Z},maximum=new[]{bounds.MaxPoint.X,bounds.MaxPoint.Y,bounds.MaxPoint.Z},
                    text=entity is MText mt?mt.Contents:entity is DBText dt?dt.TextString:null});
            }
            tables.Add(new{source.Key,metadata=CadTableTextEditor.Capture(table,()=>{}),block=table.BlockTableRecord.Handle.ToString(),graphics});
        }
        return Wire.Element(new{columns=0,cells=Array.Empty<object>(),block_ids=blockIds,model_ids=modelIds,tables});
    }
    public void Fail(Transaction tr)
    {Save("rollback-mutated.json",Snapshot(tr));throw new DomainException("TABLE_ACCEPTANCE_FAILURE","Controlled failure after native table edits and table graphics regeneration.");}
    public void VerifyRollback()
    {
        using var tr=_state.Document.Database.TransactionManager.StartTransaction();var after=Snapshot(tr);Save("rollback-after.json",after);
        TableTextVerification.Verify(_before,after,Array.Empty<TableTextCellEdit>());Save("rollback-verified.json",new{verified=true,scope="table contents and observed formatting/geometry, table graphics bounds/text/identities, model/block identities"});
    }
}
