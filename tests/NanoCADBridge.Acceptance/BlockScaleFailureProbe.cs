using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal sealed class BlockScaleFailureProbe
{
    private const string Run=@"C:\NanoCAD MCP\artifacts\phase4a-block-scale-native-v2";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase4a-block-scale-native-v2\block-scale.dxf";
    private static bool _consumed;
    private static bool _roundoffRecorded;
    public static void RecordRoundoff(double x,double y,double z,bool detached)
    {
        if(_roundoffRecorded||!Directory.Exists(Run))return;_roundoffRecorded=true;
        Save("uniform-scale-roundoff.json",new{scale=new[]{x,y,z},detached,relative_tolerance=1e-12,scope="native ScaleFactors roundoff observed; complete matrix/attributes still verified independently"});
    }
    private readonly DocumentState _state;
    private readonly CadModifier.Resolved _resolved;
    private readonly JsonElement _before;
    private BlockScaleFailureProbe(DocumentState state,CadModifier.Resolved resolved,Transaction tr)
    {_state=state;_resolved=resolved;_before=Snapshot(tr);Save("rollback-before.json",_before);}
    private static void Save(string name,object value)
    {using var file=new FileStream(Path.Combine(Run,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(file,value,Wire.Json);}
    public static BlockScaleFailureProbe? Begin(ModifyArguments args,DocumentState state,CadModifier.Resolved resolved,Transaction tr)
    {
        var plan=Path.Combine(Run,"failure-plan.json");
        if(_consumed||!File.Exists(plan)||!string.Equals(Path.GetFullPath(state.Document.Name),Target,StringComparison.OrdinalIgnoreCase))return null;
        var text=File.ReadAllText(plan);if(text.Length>16384)throw new InvalidOperationException("Excessive failure plan.");
        using var parsed=JsonDocument.Parse(text);var p=parsed.RootElement;
        if(p.GetProperty("operation_id").GetString()!=args.OperationId)return null;
        using var process=Process.GetCurrentProcess();var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        if(p.GetProperty("bridge_instance_id").GetString()!=args.BridgeId||p.GetProperty("document_id").GetString()!=args.DocumentId||
            p.GetProperty("process_id").GetInt32()!=process.Id||p.GetProperty("process_start_utc_ticks").GetInt64()!=process.StartTime.ToUniversalTime().Ticks||
            expiry<=DateTimeOffset.UtcNow||expiry>DateTimeOffset.UtcNow.AddMinutes(10)||resolved.Blocks.Count!=resolved.Sources.Count||
            !args.Operations.All(op=>op is MoveSpec or RotateSpec or ScaleSpec)||ModifyValidation.Parse(p.GetProperty("request")).Fingerprint()!=args.Fingerprint())
            throw new InvalidOperationException("Failure plan lifetime/payload/scope mismatch.");
        _consumed=true;File.Move(plan,plan+".consumed");return new(state,resolved,tr);
    }
    private JsonElement Snapshot(Transaction tr)
    {
        var db=_state.Document.Database;var blocks=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        string[] Ids(IEnumerable<ObjectId> ids)=>ids.Where(id=>!id.IsErased).Select(id=>id.Handle.ToString()).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var model=(BlockTableRecord)tr.GetObject(_resolved.Model,OpenMode.ForRead);
        var references=new List<object>();
        foreach(var source in _resolved.Sources.OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            var b=(BlockReference)tr.GetObject(source.Value,OpenMode.ForRead);var attributes=new List<object>();
            foreach(ObjectId id in b.AttributeCollection)
            {
                var a=(AttributeReference)tr.GetObject(id,OpenMode.ForRead);
                attributes.Add(new{handle=id.Handle.ToString(),owner=a.OwnerId.Handle.ToString(),a.Tag,a.TextString,a.Height,a.Rotation,
                    position=new[]{a.Position.X,a.Position.Y,a.Position.Z},a.WidthFactor,a.Oblique,style=a.TextStyleId.Handle.ToString(),layer=a.LayerId.Handle.ToString(),a.ColorIndex});
            }
            var d=(BlockTableRecord)tr.GetObject(b.BlockTableRecord,OpenMode.ForRead);
            references.Add(new{handle=source.Key,definition=d.Handle.ToString(),members=Ids(d.Cast<ObjectId>()),
                origin=new[]{d.Origin.X,d.Origin.Y,d.Origin.Z},position=new[]{b.Position.X,b.Position.Y,b.Position.Z},b.Rotation,
                scale=new[]{b.ScaleFactors.X,b.ScaleFactors.Y,b.ScaleFactors.Z},matrix=b.BlockTransform.ToArray(),attributes});
        }
        return Wire.Element(new{block_ids=Ids(blocks.Cast<ObjectId>()),model_ids=Ids(model.Cast<ObjectId>()),references});
    }
    public void Fail(Transaction tr)
    {Save("rollback-mutated.json",Snapshot(tr));throw new DomainException("BLOCK_ACCEPTANCE_FAILURE","Controlled failure after scaling block references and owned attributes.");}
    public void VerifyRollback()
    {
        using var tr=_state.Document.Database.TransactionManager.StartTransaction();var after=Snapshot(tr);Save("rollback-after.json",after);
        if(!SectionViewState.Same(_before,after))throw new InvalidOperationException("Block/attribute rollback not verified.");
        Save("rollback-verified.json",new{verified=true,scope="block placements, owned attribute content/positions/identities, definition membership and model/block inventories"});
    }
}

