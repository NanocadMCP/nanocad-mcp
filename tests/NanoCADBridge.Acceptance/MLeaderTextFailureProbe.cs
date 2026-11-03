using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Fixed one-shot diagnostic operation, absent from ordinary binaries.
internal sealed class MLeaderTextFailureProbe
{
    private const string Run=@"C:\NanoCAD MCP\artifacts\core-mleader-landing-native";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-mleader-landing-native\mleader-edit.dxf";
    private static bool _consumed;
    private static int _placements;
    internal static void RecordPlacement(bool transient,JsonElement before,JsonElement assigned,JsonElement restored)
    {
        var document=HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if(document==null||!string.Equals(Path.GetFullPath(document.Name),Target,StringComparison.OrdinalIgnoreCase)||_placements>=32)return;
        Save("placement-"+(++_placements).ToString("D2")+".json",new{transient,before,assigned,restored});
    }
    private readonly DocumentState _state;
    private readonly CadModifier.Resolved _resolved;
    private readonly JsonElement _before;
    private MLeaderTextFailureProbe(DocumentState state,CadModifier.Resolved resolved,Transaction tr)
    {_state=state;_resolved=resolved;_before=Snapshot(tr);Save("rollback-before.json",_before);}
    private static void Save(string name,object value)
    {using var file=new FileStream(Path.Combine(Run,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(file,value,Wire.Json);}
    public static MLeaderTextFailureProbe? Begin(ModifyArguments args,DocumentState state,CadModifier.Resolved resolved,Transaction tr)
    {
        var plan=Path.Combine(Run,"failure-plan.json");
        if(_consumed||!File.Exists(plan)||!string.Equals(Path.GetFullPath(state.Document.Name),Target,StringComparison.OrdinalIgnoreCase))return null;
        var text=File.ReadAllText(plan);if(text.Length>4096)throw new InvalidOperationException("Excessive failure plan.");
        using var json=JsonDocument.Parse(text);var p=json.RootElement;if(p.GetProperty("operation_id").GetString()!=args.OperationId)return null;
        using var process=Process.GetCurrentProcess();var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        if(p.GetProperty("bridge_instance_id").GetString()!=args.BridgeId||p.GetProperty("document_id").GetString()!=args.DocumentId||
            p.GetProperty("process_id").GetInt32()!=process.Id||p.GetProperty("process_start_utc_ticks").GetInt64()!=process.StartTime.ToUniversalTime().Ticks||
            expiry<=DateTimeOffset.UtcNow||expiry>DateTimeOffset.UtcNow.AddMinutes(10)||!args.Operations.All(op=>op is ReplaceMLeaderTextSpec))
            throw new InvalidOperationException("MLeader failure plan scope/lifetime mismatch.");
        _consumed=true;File.Move(plan,plan+".consumed");return new(state,resolved,tr);
    }
    private JsonElement Snapshot(Transaction tr)
    {
        var db=_state.Document.Database;var blocks=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);var model=(BlockTableRecord)tr.GetObject(_resolved.Model,OpenMode.ForRead);
        var blockIds=blocks.Cast<ObjectId>().Select(id=>id.Handle.ToString()).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var modelIds=model.Cast<ObjectId>().Select(id=>id.Handle.ToString()).OrderBy(s=>s,StringComparer.Ordinal).ToArray();var leaders=new List<object>();
        foreach(var source in _resolved.Sources.OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            var leader=(MLeader)tr.GetObject(source.Value,OpenMode.ForRead);var bounds=leader.GeometricExtents;
            leaders.Add(new{source.Key,metadata=CadMLeaderTextEditor.Capture(leader,()=>{}),minimum=new[]{bounds.MinPoint.X,bounds.MinPoint.Y,bounds.MinPoint.Z},maximum=new[]{bounds.MaxPoint.X,bounds.MaxPoint.Y,bounds.MaxPoint.Z}});
        }
        return Wire.Element(new{block_ids=blockIds,model_ids=modelIds,leaders});
    }
    public void Fail(Transaction tr)
    {Save("rollback-mutated.json",Snapshot(tr));throw new DomainException("MLEADER_ACCEPTANCE_FAILURE","Controlled failure after native MLeader text mutation.");}
    public void VerifyRollback()
    {
        using var tr=_state.Document.Database.TransactionManager.StartTransaction();var after=Snapshot(tr);Save("rollback-after.json",after);
        if(!MLeaderTextVerification.Unchanged(_before,after))throw new InvalidOperationException("Native MLeader rollback could not be verified.");
        Save("rollback-verified.json",new{verified=true,scope="MLeader observed contents/style/placement/vertices/native extents, model and block identities"});
    }
}
