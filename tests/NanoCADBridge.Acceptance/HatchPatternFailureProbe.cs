using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal sealed class HatchPatternFailureProbe
{
    private const string Run=@"C:\NanoCAD MCP\artifacts\core-hatch-spacing-native";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-hatch-spacing-native\hatch-pattern-edit.dxf";
    private static bool _consumed;
    private readonly DocumentState _state;
    private readonly CadModifier.Resolved _resolved;
    private readonly JsonElement _before;
    private HatchPatternFailureProbe(DocumentState state,CadModifier.Resolved resolved,Transaction tr)
    {_state=state;_resolved=resolved;_before=Snapshot(tr);Save("rollback-before.json",_before);}
    private static void Save(string name,object value)
    {using var file=new FileStream(Path.Combine(Run,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(file,value,Wire.Json);}
    public static HatchPatternFailureProbe? Begin(ModifyArguments args,DocumentState state,CadModifier.Resolved resolved,Transaction tr)
    {
        var plan=Path.Combine(Run,"failure-plan.json");
        if(_consumed||!File.Exists(plan)||!string.Equals(Path.GetFullPath(state.Document.Name),Target,StringComparison.OrdinalIgnoreCase))return null;
        var text=File.ReadAllText(plan);if(text.Length>16384)throw new InvalidOperationException("Excessive failure plan.");
        using var parsed=JsonDocument.Parse(text);var p=parsed.RootElement;
        if(p.GetProperty("operation_id").GetString()!=args.OperationId)return null;
        using var process=Process.GetCurrentProcess();var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        if(p.GetProperty("bridge_instance_id").GetString()!=args.BridgeId||p.GetProperty("document_id").GetString()!=args.DocumentId||
            p.GetProperty("process_id").GetInt32()!=process.Id||p.GetProperty("process_start_utc_ticks").GetInt64()!=process.StartTime.ToUniversalTime().Ticks||
            expiry<=DateTimeOffset.UtcNow||expiry>DateTimeOffset.UtcNow.AddMinutes(10)||!args.Operations.All(op=>op is SetHatchPatternSpec)||
            ModifyValidation.Parse(p.GetProperty("request")).Fingerprint()!=args.Fingerprint())throw new InvalidOperationException("Failure plan lifetime/payload/scope mismatch.");
        _consumed=true;File.Move(plan,plan+".consumed");return new(state,resolved,tr);
    }
    private JsonElement Snapshot(Transaction tr)
    {
        var db=_state.Document.Database;var blocks=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        string[] Ids(IEnumerable<ObjectId> ids)=>ids.Where(id=>!id.IsErased).Select(id=>id.Handle.ToString()).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var model=(BlockTableRecord)tr.GetObject(_resolved.Model,OpenMode.ForRead);var hatches=new List<object>();
        foreach(var source in _resolved.Sources.OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            var hatch=(Hatch)tr.GetObject(source.Value,OpenMode.ForRead);int count=hatch.NumberOfHatchLines;
            if(count is <1 or >2000)throw new InvalidOperationException("Expected bounded evaluated Hatch lines.");
            var lines=new List<double[]>();
            for(int i=0;i<count;i++)
            {
                using var line=hatch.GetHatchLineDataAt(i);var p=line.PointOnLine;var d=line.Direction;double norm=Math.Sqrt(d.X*d.X+d.Y*d.Y);
                if(!double.IsFinite(norm)||norm<1e-12)throw new InvalidOperationException("Invalid hatch line direction.");
                double x=d.X/norm,y=d.Y/norm;if(x<0||x==0&&y<0){x=-x;y=-y;}
                double offset=-y*p.X+x*p.Y;if(!double.IsFinite(offset))throw new InvalidOperationException("Invalid hatch line origin.");
                lines.Add(new[]{x,y,offset});
            }
            hatches.Add(new{handle=source.Key,state=CadHatchPatternEditor.Capture(hatch,()=>{}).Snapshot,
                generated=lines.OrderBy(l=>l[0]).ThenBy(l=>l[1]).ThenBy(l=>l[2]).ToArray()});
        }
        return Wire.Element(new{block_ids=Ids(blocks.Cast<ObjectId>()),model_ids=Ids(model.Cast<ObjectId>()),hatches});
    }
    public void Fail(Transaction tr)
    {Save("rollback-mutated.json",Snapshot(tr));throw new DomainException("HATCH_ACCEPTANCE_FAILURE","Controlled failure after pattern setters and native evaluation.");}
    public void VerifyRollback()
    {
        using var tr=_state.Document.Database.TransactionManager.StartTransaction();var after=Snapshot(tr);Save("rollback-after.json",after);
        if(!SectionViewState.Same(_before,after))throw new InvalidOperationException("Hatch parameters/generated-line rollback not verified.");
        Save("rollback-verified.json",new{verified=true,scope="Hatch pattern parameters, boundary/origin/properties, evaluated line counts/directions/offsets and model/block inventories"});
    }
}
