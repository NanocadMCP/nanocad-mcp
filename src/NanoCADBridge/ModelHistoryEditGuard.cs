using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Preserve the remaining native design when a nonterminal feature changes.
// Generated edge markers can change during rebuild; feature identities,
// parameters, order and external source bindings must remain intact.
internal static class ModelHistoryEditGuard
{
    // A managed MultiCAD GUID's Handle field is not necessarily its current
    // DWG representation handle. Resolve through the active native document,
    // just as the manufacturing editors do after a representation replacement.
    internal static Db.ObjectId CurrentRepresentation(Multicad.Mc3D.Mc3dSolid body,DocumentState state,Db.Transaction tx,Action? cancel=null)
    {
        var active=Multicad.DatabaseServices.McDocument.ActiveDocument;
        if(active==null||!state.Document.IsNamedDrawing||string.IsNullOrWhiteSpace(active.Path)||
            !string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH","The active native model document does not match this named DWG.");
        var model=((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        var matches=((Db.BlockTableRecord)tx.GetObject(model,Db.OpenMode.ForRead)).Cast<Db.ObjectId>()
            .Where(id=>{cancel?.Invoke();return !id.IsErased&&!id.IsEffectivelyErased&&tx.GetObject(id,Db.OpenMode.ForRead) is Db.Solid3d&&
                McObjectId.FromHandle(id.Handle.Value,active.ID)==body.ID;}).ToArray();
        if(matches.Length!=1)throw new DomainException("OBJECT_IDENTITY_MISMATCH","The native body has no unique current top-level DWG representation.");
        return matches[0];
    }
    internal static JsonElement FeatureNodes(Multicad.Mc3D.Mc3dSolid body,DocumentState state,McObjectId[] features,Action? cancel=null)
    {
        if(features.Length==0)return Wire.Element(Array.Empty<object>());
        string handle;
        using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            handle=Validation.NormalizeHandle(CurrentRepresentation(body,state,tx,cancel).Handle.ToString());
        var snapshot=ModelParameterSnapshot.Capture(new ModelParameterArguments("","","","","Millimeters",handle,"","",0),state,cancel);
        ModelParameterSnapshot.Healthy(snapshot);
        var wanted=features.Select(id=>id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nodes=JsonNode.Parse(snapshot.GetProperty("nodes").GetRawText())!.AsArray();
        var kept=new List<JsonNode>();
        foreach(var node in nodes)
        {
            cancel?.Invoke();
            if(!wanted.Contains(node!["mc_id"]!.GetValue<string>()))continue;
            if(node["parameters"] is JsonObject parameters&&parameters["edge_ids"] is JsonArray edges)
            {parameters["edge_count"]=edges.Count;parameters.Remove("edge_ids");}
            kept.Add(JsonNode.Parse(node.ToJsonString())!);
        }
        if(kept.Count!=features.Length)throw new DomainException("HISTORY_UNAVAILABLE","Retained feature metadata is incomplete.");
        return Wire.Element(kept);
    }
    internal static JsonElement Capture(ModifyArguments args,DocumentState state,McObjectId target,string? handle=null,Action? cancel=null)
    {
        var snapshot=ModelParameterSnapshot.Capture(new ModelParameterArguments(args.BridgeId,args.DocumentId,
            args.ExpectedRevision,args.OperationId,args.Units,handle??args.Operations.Single().Handles.Single(),"","",0),state,cancel);
        ModelParameterSnapshot.Healthy(snapshot);
        var affected=ModelDependencyGraph410.Closure(snapshot,[target.ToString()],cancel);
        var nodes=ModelDependencyGraph410.ProtectedNodes(snapshot,affected,replaceTargetParameters:new HashSet<string>(StringComparer.OrdinalIgnoreCase){target.ToString()},cancel:cancel);
        return Wire.Element(new{body=snapshot.GetProperty("body"),last_item=snapshot.GetProperty("last_item"),
            external_sources=snapshot.GetProperty("external_sources"),nodes});
    }
    internal static void Verify(JsonElement before,ModifyArguments args,DocumentState state,McObjectId target,string handle,Action? cancel=null)
    {
        if(!ModelParameterSnapshot.Equivalent(before,Capture(args,state,target,handle,cancel)))
            throw new DomainException("MODEL_STATE_UNAVAILABLE","Editing the feature changed another history item, its parameters or external source bindings.");
    }
}
