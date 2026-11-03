using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal sealed class DimensionTextFailureProbe
{
    private const string Run=@"C:\NanoCAD MCP\artifacts\core-dimension-manual-cache-native";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-dimension-manual-cache-native\dimension-text.dxf";
    private static bool _consumed;
    private static int _comparisons;
    public static void RecordComparison(object value)
    {
        var document=HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if(document==null||!string.Equals(Path.GetFullPath(document.Name),Target,StringComparison.OrdinalIgnoreCase)||_comparisons>=32)return;
        Save("comparison-"+(++_comparisons).ToString("D2")+".json",value);
    }
    private readonly DocumentState _state;
    private readonly CadModifier.Resolved _resolved;
    private readonly JsonElement _before;
    private DimensionTextFailureProbe(DocumentState state,CadModifier.Resolved resolved,Transaction tr)
    { _state=state;_resolved=resolved;_before=Snapshot(tr);Save("rollback-before.json",_before); }
    private static void Save(string name,object value)
    {using var file=new FileStream(Path.Combine(Run,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(file,value,Wire.Json);}
    public static DimensionTextFailureProbe? Begin(ModifyArguments args,DocumentState state,CadModifier.Resolved resolved,Transaction tr)
    {
        var plan=Path.Combine(Run,"failure-plan.json");
        if(_consumed || !File.Exists(plan) || !string.Equals(Path.GetFullPath(state.Document.Name),Target,StringComparison.OrdinalIgnoreCase))return null;
        var text=File.ReadAllText(plan);if(text.Length>16384)throw new InvalidOperationException("Excessive failure plan.");
        using var parsed=JsonDocument.Parse(text);var p=parsed.RootElement;
        if(p.GetProperty("operation_id").GetString()!=args.OperationId)return null;
        using var process=Process.GetCurrentProcess();var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        if(p.GetProperty("bridge_instance_id").GetString()!=args.BridgeId || p.GetProperty("document_id").GetString()!=args.DocumentId ||
            p.GetProperty("process_id").GetInt32()!=process.Id || p.GetProperty("process_start_utc_ticks").GetInt64()!=process.StartTime.ToUniversalTime().Ticks ||
            expiry<=DateTimeOffset.UtcNow || expiry>DateTimeOffset.UtcNow.AddMinutes(10) || !args.Operations.All(op=>op is ReplaceDimensionTextSpec or SetDimensionLineSpec) ||
            ModifyValidation.Parse(p.GetProperty("request")).Fingerprint()!=args.Fingerprint())
            throw new InvalidOperationException("Failure plan lifetime/payload/scope mismatch.");
        _consumed=true;File.Move(plan,plan+".consumed");var probe=new DimensionTextFailureProbe(state,resolved,tr);return probe;
    }
    private JsonElement Snapshot(Transaction tr)
    {
        var db=_state.Document.Database;var blocks=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        var blockIds=blocks.Cast<ObjectId>().Where(id=>!id.IsErased).Select(id=>id.Handle.ToString()).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var model=(BlockTableRecord)tr.GetObject(_resolved.Model,OpenMode.ForRead);
        var modelIds=model.Cast<ObjectId>().Where(id=>!id.IsErased).Select(id=>id.Handle.ToString()).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var dimensions=new List<object>();
        foreach(var source in _resolved.Sources.OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            var d=(Dimension)tr.GetObject(source.Value,OpenMode.ForRead);var block=(BlockTableRecord)tr.GetObject(d.DimBlockId,OpenMode.ForRead);
            var graphics=new List<object>();
            foreach(ObjectId id in block)
            {
                if(id.IsErased)continue;
                if(graphics.Count>=512)throw new InvalidOperationException("Dimension graphics audit exceeded512 entities.");
                var entity=(Entity)tr.GetObject(id,OpenMode.ForRead);var bounds=entity.GeometricExtents;
                graphics.Add(new{handle=id.Handle.ToString(),type=entity.GetType().FullName,layer=entity.LayerId.Handle.ToString(),color=entity.ColorIndex,
                    minimum=new[]{bounds.MinPoint.X,bounds.MinPoint.Y,bounds.MinPoint.Z},maximum=new[]{bounds.MaxPoint.X,bounds.MaxPoint.Y,bounds.MaxPoint.Z},
                    text=entity is MText mt?mt.Contents:entity is DBText dt?dt.TextString:null});
            }
            dimensions.Add(new{handle=source.Key,definition=DimensionGeometryReader.Definition(d),text=d.DimensionText,style=d.DimensionStyle.Handle.ToString(),
                text_position=new[]{d.TextPosition.X,d.TextPosition.Y,d.TextPosition.Z},rotation=d.TextRotation,default_position=d.UsingDefaultTextPosition,block=d.DimBlockId.Handle.ToString(),graphics});
        }
        return Wire.Element(new{block_ids=blockIds,model_ids=modelIds,dimensions});
    }
    public void Fail(Transaction tr)
    {Save("rollback-mutated.json",Snapshot(tr));throw new DomainException("DIMENSION_ACCEPTANCE_FAILURE","Controlled failure after dimension placement/text editing and native block regeneration.");}
    public void VerifyRollback()
    {
        using var tr=_state.Document.Database.TransactionManager.StartTransaction();var after=Snapshot(tr);Save("rollback-after.json",after);
        if(!SectionViewState.Same(_before,after))throw new InvalidOperationException("Dimension content/graphics rollback not verified.");
        Save("rollback-verified.json",new{verified=true,scope="dimension definitions, text/style/placement, native graphics bounds/text/identities and model/block inventories"});
    }
}
