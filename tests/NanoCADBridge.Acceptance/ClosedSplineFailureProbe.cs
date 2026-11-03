using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal sealed class ClosedSplineFailureProbe
{
    private const string Run=@"C:\NanoCAD MCP\artifacts\core-closed-spline-edit-probe-native-115";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-closed-spline-edit-probe-native-115\closed-edit.dxf";
    private static bool _consumed;
    private readonly DocumentState _state;
    private readonly CadModifier.Resolved _resolved;
    private readonly JsonElement _before;
    private ClosedSplineFailureProbe(DocumentState state,CadModifier.Resolved resolved,Transaction tr)
    {_state=state;_resolved=resolved;_before=Snapshot(tr);Save("rollback-before.json",_before);}
    private static void Save(string name,object value)
    {using var file=new FileStream(Path.Combine(Run,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(file,value,Wire.Json);}
    public static ClosedSplineFailureProbe? Begin(ModifyArguments args,DocumentState state,CadModifier.Resolved resolved,Transaction tr)
    {
        var plan=Path.Combine(Run,"failure-plan.json");
        if(_consumed||!File.Exists(plan)||!string.Equals(Path.GetFullPath(state.Document.Name),Target,StringComparison.OrdinalIgnoreCase))return null;
        var text=File.ReadAllText(plan);if(text.Length>16384)throw new InvalidOperationException("Excessive failure plan.");
        using var parsed=JsonDocument.Parse(text);var p=parsed.RootElement;
        if(p.GetProperty("operation_id").GetString()!=args.OperationId)return null;
        using var process=Process.GetCurrentProcess();var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        if(p.GetProperty("bridge_instance_id").GetString()!=args.BridgeId||p.GetProperty("document_id").GetString()!=args.DocumentId||
            p.GetProperty("process_id").GetInt32()!=process.Id||p.GetProperty("process_start_utc_ticks").GetInt64()!=process.StartTime.ToUniversalTime().Ticks||
            expiry<=DateTimeOffset.UtcNow||expiry>DateTimeOffset.UtcNow.AddMinutes(10)||resolved.Sources.Count!=2||!args.Operations.All(op=>op is MoveSpec or RotateSpec or ScaleSpec or CopySpec)||
            ModifyValidation.Parse(p.GetProperty("request")).Fingerprint()!=args.Fingerprint())throw new InvalidOperationException("Failure plan lifetime/payload/scope mismatch.");
        _consumed=true;File.Move(plan,plan+".consumed");return new(state,resolved,tr);
    }
    private JsonElement Snapshot(Transaction tr)
    {
        var db=_state.Document.Database;var blocks=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        string[] Ids(IEnumerable<ObjectId> ids)=>ids.Where(id=>!id.IsErased).Select(id=>id.Handle.ToString()).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var model=(BlockTableRecord)tr.GetObject(_resolved.Model,OpenMode.ForRead);var rows=new List<object>();
        foreach(var id in model.Cast<ObjectId>().Where(id=>!id.IsErased&&!id.IsEffectivelyErased).OrderBy(id=>id.Handle.ToString(),StringComparer.Ordinal))
        {
            var entity=(Entity)tr.GetObject(id,OpenMode.ForRead);
            if(entity.GetType()!=typeof(Spline)&&entity.GetType()!=typeof(Line))throw new InvalidOperationException("Only own spline/line fixture allowed.");
            using var reactors=entity.GetPersistentReactorIds();
            rows.Add(new{handle=entity.Handle.ToString(),owner=entity.OwnerId.Handle.ToString(),entity.HasFields,
                reactors=reactors.Cast<ObjectId>().Select(x=>x.Handle.ToString()).ToArray(),
                detail=EntityAdapter.Detail(entity,EntityAdapter.Summary(entity,_resolved.Model,"probe","probe"),CancellationToken.None)});
        }
        return Wire.Element(new{block_ids=Ids(blocks.Cast<ObjectId>()),model_ids=Ids(model.Cast<ObjectId>()),entities=rows});
    }
    public void Fail(Transaction tr)
    {Save("rollback-mutated.json",Snapshot(tr));throw new DomainException("SPLINE_ACCEPTANCE_FAILURE","Controlled failure after closed spline transforms and copies.");}
    public void VerifyRollback()
    {
        using var tr=_state.Document.Database.TransactionManager.StartTransaction();var after=Snapshot(tr);Save("rollback-after.json",after);
        if(!SectionViewState.Same(_before,after))throw new InvalidOperationException("Closed spline NURBS, properties or inventory rollback not verified.");
        Save("rollback-verified.json",new{verified=true,scope="all native NURBS definitions/flags/properties and source/control/copy model inventory"});
    }
}
