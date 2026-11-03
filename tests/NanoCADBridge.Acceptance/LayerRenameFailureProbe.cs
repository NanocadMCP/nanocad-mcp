using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal sealed class LayerRenameFailureProbe
{
    private const string Run=@"C:\NanoCAD MCP\artifacts\phase4a-layer-rename-native";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase4a-layer-rename-native\layer-rename.dxf";
    private static bool _consumed;
    private readonly DocumentState _state;
    private readonly string[] _names;
    private readonly JsonElement _before;
    private LayerRenameFailureProbe(DocumentState state,StructureWriteArguments args,Transaction tr)
    {_state=state;_names=args.Operations.Cast<RenameLayerEdit>().SelectMany(e=>new[]{e.ExpectedName,e.Name}).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n=>n,StringComparer.Ordinal).ToArray();_before=Snapshot(tr);Save("rollback-before.json",_before);}
    private static void Save(string name,object value)
    {using var file=new FileStream(Path.Combine(Run,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(file,value,Wire.Json);}
    public static LayerRenameFailureProbe? Begin(StructureWriteArguments args,DocumentState state,Transaction tr)
    {
        var plan=Path.Combine(Run,"failure-plan.json");
        if(_consumed||!File.Exists(plan)||!string.Equals(Path.GetFullPath(state.Document.Name),Target,StringComparison.OrdinalIgnoreCase))return null;
        var text=File.ReadAllText(plan);if(text.Length>16384)throw new InvalidOperationException("Excessive failure plan.");
        using var parsed=JsonDocument.Parse(text);var p=parsed.RootElement;
        if(p.GetProperty("operation_id").GetString()!=args.OperationId)return null;
        using var process=Process.GetCurrentProcess();var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        if(p.GetProperty("bridge_instance_id").GetString()!=args.BridgeId||p.GetProperty("document_id").GetString()!=args.DocumentId||
            p.GetProperty("process_id").GetInt32()!=process.Id||p.GetProperty("process_start_utc_ticks").GetInt64()!=process.StartTime.ToUniversalTime().Ticks||
            expiry<=DateTimeOffset.UtcNow||expiry>DateTimeOffset.UtcNow.AddMinutes(10)||!args.Operations.All(op=>op is RenameLayerEdit)||
            StructureWriteValidation.Parse(p.GetProperty("request")).Fingerprint()!=args.Fingerprint())throw new InvalidOperationException("Failure plan lifetime/payload/scope mismatch.");
        _consumed=true;File.Move(plan,plan+".consumed");return new(state,args,tr);
    }
    private JsonElement Snapshot(Transaction tr)
    {
        var db=_state.Document.Database;var table=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForRead);var layers=new List<object>();
        foreach(ObjectId id in table)
        {
            if(id.IsErased)continue;if(layers.Count>=32)throw new InvalidOperationException("Layer fixture inventory exceeded32.");
            var layer=(LayerTableRecord)tr.GetObject(id,OpenMode.ForRead);layers.Add(new{handle=id.Handle.ToString(),name=layer.Name,properties=CadLayerRenamer.Properties(layer)});
        }
        var index=_names.Select(name=>new{name,handle=table.Has(name)?table[name].Handle.ToString():null}).ToArray();
        var blocks=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);var model=(BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.ModelSpace],OpenMode.ForRead);
        var objects=new List<object>();foreach(ObjectId id in model)
        {if(id.IsErased)continue;var e=(Entity)tr.GetObject(id,OpenMode.ForRead);objects.Add(new{handle=id.Handle.ToString(),type=e.GetType().FullName,layer=e.LayerId.Handle.ToString()});}
        return Wire.Element(new{current_layer=db.Clayer.Handle.ToString(),layers,index,objects});
    }
    public void Fail(Transaction tr)
    {Save("rollback-mutated.json",Snapshot(tr));throw new DomainException("LAYER_ACCEPTANCE_FAILURE","Controlled failure after resident layer rename and name-index update.");}
    public void VerifyRollback()
    {
        using var tr=_state.Document.Database.TransactionManager.StartTransaction();var after=Snapshot(tr);Save("rollback-after.json",after);
        if(!SectionViewState.Same(_before,after))throw new InvalidOperationException("Layer identity/name-index/properties rollback not verified.");
        Save("rollback-verified.json",new{verified=true,scope="all layer identities/names/properties, old/new table name indices, current-layer identity, Model Space inventory and LayerId assignments"});
    }
}
