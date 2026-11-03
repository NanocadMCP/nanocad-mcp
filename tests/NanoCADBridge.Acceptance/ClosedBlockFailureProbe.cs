using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal sealed class ClosedBlockFailureProbe
{
    private const string Run=@"C:\NanoCAD MCP\artifacts\core-closed-spline-blocks-probe-native-117";
    private const string Target=@"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-closed-spline-blocks-probe-native-117\closed-blocks.dxf";
    private static readonly HashSet<string> Used=new(StringComparer.Ordinal);
    private readonly DocumentState _state;
    private readonly string _phase;
    private readonly JsonElement _before;
    private ClosedBlockFailureProbe(DocumentState state,string phase,Transaction tr)
    {_state=state;_phase=phase;_before=Snapshot(tr);Save("before",_before);}
    private void Save(string name,object value)
    {using var f=new FileStream(Path.Combine(Run,_phase+"-"+name+".json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(f,value,Wire.Json);}
    public static ClosedBlockFailureProbe? Begin(IWriteArguments args,DocumentState state,Transaction tr)
    {
        if(!string.Equals(Path.GetFullPath(state.Document.Name),Target,StringComparison.OrdinalIgnoreCase))return null;
        string? phase=args switch
        {
            StructureWriteArguments s when s.Operations.Length==1 && s.Operations.All(o=>o is DefineBlockEdit d && d.Attributes.Length==0 && d.Entities.Any(e=>e is SplineSpec{Closed:true}))=>"define",
            StructureWriteArguments s when s.Operations.Length is >=1 and <=3 && s.Operations.All(o=>o is InsertBlockEdit)=>"insert",
            ModifyArguments m when m.Operations.Length is >=1 and <=5 && m.Operations.All(o=>o is MoveSpec or RotateSpec or ScaleSpec or CopySpec or EraseSpec)=>"edit",
            _=>null
        };
        if(phase==null||Used.Contains(phase))return null;
        var path=Path.Combine(Run,"failure-plan-"+phase+".json");if(!File.Exists(path))return null;
        var text=File.ReadAllText(path);if(text.Length>16384)throw new InvalidOperationException("Excessive plan.");
        using var parsed=JsonDocument.Parse(text);var p=parsed.RootElement;
        if(p.GetProperty("operation_id").GetString()!=args.OperationId)return null;
        using var process=Process.GetCurrentProcess();var expiry=p.GetProperty("expires_at_utc").GetDateTimeOffset();
        if(p.GetProperty("bridge_instance_id").GetString()!=args.BridgeId||p.GetProperty("document_id").GetString()!=args.DocumentId||
            p.GetProperty("process_id").GetInt32()!=process.Id||p.GetProperty("process_start_utc_ticks").GetInt64()!=process.StartTime.ToUniversalTime().Ticks||
            expiry<=DateTimeOffset.UtcNow||expiry>DateTimeOffset.UtcNow.AddMinutes(10)||WriteValidation.Parse(args.Operation,p.GetProperty("request")).Fingerprint()!=args.Fingerprint())
            throw new InvalidOperationException("Failure plan lifetime/payload mismatch.");
        Used.Add(phase);File.Move(path,path+".consumed");return new(state,phase,tr);
    }
    private JsonElement Snapshot(Transaction tr)
    {
        var db=_state.Document.Database;var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);var model=table[BlockTableRecord.ModelSpace];var blocks=new List<object>();int count=0;
        object Dependencies(DBObject value)
        {using var ids=value.GetPersistentReactorIds();return new{value.HasFields,reactors=ids.Cast<ObjectId>().Select(i=>i.Handle.ToString()).OrderBy(s=>s,StringComparer.Ordinal).ToArray()};}
        foreach(var id in table.Cast<ObjectId>().Where(i=>!i.IsErased&&!i.IsEffectivelyErased).OrderBy(i=>i.Handle.ToString(),StringComparer.Ordinal))
        {
            if(blocks.Count>=16)throw new InvalidOperationException("Excessive block inventory.");
            var b=(BlockTableRecord)tr.GetObject(id,OpenMode.ForRead);var entities=new List<object>();
            foreach(var member in b.Cast<ObjectId>().Where(i=>!i.IsErased&&!i.IsEffectivelyErased).OrderBy(i=>i.Handle.ToString(),StringComparer.Ordinal))
            {
                if(++count>256)throw new InvalidOperationException("Excessive entity inventory.");
                var e=(Entity)tr.GetObject(member,OpenMode.ForRead);object geometry;
                if(e.GetType()==typeof(BlockReference))
                {
                    var r=(BlockReference)e;
                    if(r.IsDynamicBlock||r.AttributeCollection.Cast<ObjectId>().Any())throw new InvalidOperationException("Only attribute-free static references in own fixture.");
                    geometry=new{definition=r.BlockTableRecord.Handle.ToString(),position=new[]{r.Position.X,r.Position.Y,r.Position.Z},r.Rotation,
                        scale=new[]{r.ScaleFactors.X,r.ScaleFactors.Y,r.ScaleFactors.Z},normal=new[]{r.Normal.X,r.Normal.Y,r.Normal.Z},matrix=r.BlockTransform.ToArray()};
                }
                else if(e.GetType()==typeof(Line)||e.GetType()==typeof(Spline))
                    geometry=EntityAdapter.Detail(e,EntityAdapter.Summary(e,model,"probe","probe"),CancellationToken.None);
                else throw new InvalidOperationException("Unexpected entity in own closed block fixture.");
                entities.Add(new{handle=e.Handle.ToString(),owner=e.OwnerId.Handle.ToString(),geometry,properties=CadBlockMover.PreservedProperties(e),dependencies=Dependencies(e)});
            }
            blocks.Add(new{handle=b.Handle.ToString(),owner=b.OwnerId.Handle.ToString(),b.Name,b.Units,b.IsLayout,b.IsAnonymous,b.IsDynamicBlock,
                origin=new[]{b.Origin.X,b.Origin.Y,b.Origin.Z},b.Explodable,b.BlockScaling,dependencies=Dependencies(b),entities});
        }
        return Wire.Element(new{blocks});
    }
    public void Fail(Transaction tr)
    {Save("mutated",Snapshot(tr));throw new DomainException("CLOSED_BLOCK_ACCEPTANCE_FAILURE","Controlled failure after resident attribute-free closed block changes.");}
    public void VerifyRollback()
    {
        using var tr=_state.Document.Database.TransactionManager.StartTransaction();var after=Snapshot(tr);Save("after",after);
        if(!SectionViewState.Same(_before,after))throw new InvalidOperationException("Closed block full rollback not verified.");
        Save("verified",new{verified=true,scope="complete block/name/member inventory, original/control placements and properties, closed NURBS definitions and persistent dependencies"});
    }
}
