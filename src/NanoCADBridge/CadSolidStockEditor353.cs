using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Brep=Teigha.BoundaryRepresentation.Brep;

namespace NanoCADBridge;

// Native body operations, never coordinate scaling or target replacement.
// Caller retains existing direct/history/dependency/layer admission and shared
// post-Commit documentation refresh. Opaque owned internals are not measured.
internal static class CadSolidStockEditor353
{
    internal static SolidStockGeometry353 Measure(Solid3d body,Action cancel)
    {
        CadSolidEditor.CheckGeometry(body,cancel);
        var box=body.GeometricExtents;
        cancel();using var brep=new Brep(body);
        bool? healthy;
        // Only the optional native check can become an unavailable observation.
        // Construction, cancellation and wrapper disposal remain critical.
        try{healthy=brep.CheckEntity();}
        catch(Teigha.Runtime.Exception error) when(error.ErrorStatus.ToString() is "NotImplemented" or "NotImplementedYet")
        {healthy=null;}
        cancel();
        if(healthy==false)throw new DomainException("INVALID_GEOMETRY","Native BRep validation reports an invalid direct wall/stock result.");
        return new(body.MassProperties.Volume,new(
            [box.MinPoint.X,box.MinPoint.Y,box.MinPoint.Z],
            [box.MaxPoint.X,box.MaxPoint.Y,box.MaxPoint.Z]),healthy,
            healthy.HasValue?null:"Native BRep validation is not implemented for this modeler; finite nonempty geometry and root preservation are observed.");
    }
    internal static JsonElement RootState(Solid3d body,Action cancel)=>Preserved(body,cancel);
    private static JsonElement Preserved(Solid3d body,Action cancel)
    {
        cancel();using var reactors=body.GetPersistentReactorIds();using var data=body.XData;
        var links=new List<string>();foreach(ObjectId id in reactors){cancel();links.Add(id.ToString());}
        var typed=new List<object>();var budget=new ModelInventoryBudget(cancel);
        if(data!=null)foreach(var value in data.AsArray())
        {
            cancel();var item=new{code=value.TypeCode,value=NativeOwnedMetadata.Value(value.Value)};
            budget.Add(item);typed.Add(item);
        }
        var observed=Wire.Element(new{native_type=body.GetType().FullName,
            identity=body.ObjectId.ToString(),owner=body.OwnerId.ToString(),
            layer=body.LayerId.ToString(),linetype=body.LinetypeId.ToString(),material=body.MaterialId.ToString(),
            display=EntityAdapter.StoredProperties(body),body.RecordHistory,body.ShowHistory,body.HasFields,
            extension_dictionary=body.ExtensionDictionary.ToString(),
            reactors=links.OrderBy(value=>value,StringComparer.Ordinal).ToArray(),xdata=typed});
        budget.Add(observed);return observed;
    }
    private static SolidStockGeometry353 Verify(JsonElement before,Solid3d body,Action cancel)
    {
        CadSolidEditor.CheckGeometry(body,cancel);
        if(!ModelParameterSnapshot.Equivalent(before,Preserved(body,cancel)))
            throw new DomainException("OBJECT_IDENTITY_MISMATCH","Native wall/stock editing changed the body root identity, display, XData or dependency identities.");
        return Measure(body,cancel);
    }
    internal static SolidStockGeometry353 Shell(SolidFinishingCopySpec spec,Solid3d body,SubentityId[] removed,Action cancel)
    {
        var before=Preserved(body,cancel);cancel();
        double offset=SolidShellSide353.NativeOffset(spec.ShellSide,spec.Size);
        if(removed.Length!=0)body.ShellBody(removed,offset);
        else
        {
            // The installed kernel rejects ShellBody with no opening faces.
            // A closed wall is the native difference between the original
            // solid and its normal offset. No primitive/coordinate rebuilding,
            // detached entity registration or resident target replacement.
            using var interior=(Solid3d)body.Clone();
            if(!interior.ObjectId.IsNull)
                throw new DomainException("OBJECT_IDENTITY_MISMATCH","Closed-shell offset requires an independent detached clone of the original solid.");
            cancel();
            if(offset<0)
            {
                interior.OffsetBody(offset);cancel();
                CadSolidEditor.CheckGeometry(interior,cancel);
            }
            else
            {
                body.OffsetBody(offset);cancel();
                CadSolidEditor.CheckGeometry(body,cancel);
            }
            body.BooleanOperation(BooleanOperationType.BoolSubtract,interior);
            cancel();
        }
        return Verify(before,body,cancel);
    }
    internal static (SolidStockGeometry353 Before,SolidStockGeometry353 After) Offset(
        SolidOffsetEdit353 spec,Solid3d body,EntityRef reference,Action cancel)
    {
        var snapshot=Wire.Element(SolidTopologyReader.Capture(body,reference,CancellationToken.None,cancel));
        if(snapshot.GetProperty("snapshot_token").GetString()!=spec.TopologyToken)
            throw new DomainException("STALE_TOPOLOGY","Reacquire the direct body's current topology before its whole-body offset.");
        var before=Measure(body,cancel);var preserved=Preserved(body,cancel);
        cancel();body.OffsetBody(spec.Distance);var after=Verify(preserved,body,cancel);
        return(before,after);
    }
    internal static SolidStockChange353 ShellChange(int index,SolidFinishingEditSpec spec,EntityRef source,
        SolidStockGeometry353 before,SolidStockGeometry353 after)=>new(index,spec.Kind,source,
            spec.Subentities.Length==0?"closed_shell":"removed_opening_faces",spec.ShellSide,
            SolidShellSide353.NativeOffset(spec.ShellSide,spec.Size),spec.Subentities,before,after,
            construction_method:spec.Subentities.Length==0?"native_offset_difference":"native_shell_body");
    internal static SolidStockChange353 OffsetChange(int index,SolidOffsetEdit353 spec,EntityRef source,
        SolidStockGeometry353 before,SolidStockGeometry353 after)=>new(index,spec.Operation,source,
            "whole_body_offset",spec.Distance>0?"outward":"inward",spec.Distance,Array.Empty<string>(),before,after,
            construction_method:"native_offset_body");
}
