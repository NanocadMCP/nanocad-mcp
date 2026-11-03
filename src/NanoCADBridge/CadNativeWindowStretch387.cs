using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// One useful native group: explicit independent source candidates, actual
// stretch-point window selection, complete prewrite snapshots and same roots.
internal static class CadNativeWindowStretch387
{
    private sealed record Item(string Handle,Curve Source,CadNativeStretch345.State Before,NativeStretchEdit345 Edit);
    private const string WindowWarning="NATIVE_WINDOW_STRETCH: The inclusive WCS rectangle selected actual native stretch points only within the explicit source handles. Candidates with no selected points remain unchanged; no other document object was selected. Native recovery is not guaranteed.";
    private static void Need([DoesNotReturnIf(false)] bool valid,string message,string code="OUT_OF_SCOPE")
    {if(!valid)throw new DomainException(code,message);}
    private static bool Same(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    private static bool SameState(CadNativeStretch345.State a,CadNativeStretch345.State b)
        =>Same(a.Shape,b.Shape)&&Same(a.Metadata,b.Metadata)&&Same(a.Display,b.Display)&&Same(a.Invariant,b.Invariant)&&Same(Wire.Element(a.Points),Wire.Element(b.Points));
    private static void VerifyPoints(Item item,CadNativeStretch345.State after,Action cancel)
    {
        var expected=NativeStretchRead345.Expected(item.Before.Points,item.Edit,cancel);
        Need(after.Points.Length==expected.Length,"Window stretch changed an actual native point inventory.","NATIVE_STRETCH_MISMATCH");
        for(int i=0;i<expected.Length;i++)
        {
            cancel();var a=after.Points[i].point_wcs;var b=expected[i].point_wcs;
            Need(new Point3d(a[0],a[1],a[2]).DistanceTo(new Point3d(b[0],b[1],b[2]))<=Tolerance.Global.EqualPoint,
                "Final window stretch did not retain every selected displacement and unselected native point.","NATIVE_STRETCH_MISMATCH");
        }
    }
    private static Item Prepare(Entity source,string handle,NativeWindowStretchEdit387 edit,Database db,Transaction tr,Action cancel)
    {
        Need(source is Curve&&CadNativeStretch345.Supported(source.GetType()),"Window stretch requires exact native Line/Polyline candidates.","UNSUPPORTED_ENTITY_TYPE");
        var curve=(Curve)source;CadNativeStretch345.Ownership(curve,db,tr,cancel);var before=CadNativeStretch345.Capture(curve,tr,cancel);
        int ordinal=Array.IndexOf(edit.Handles,handle);Need(ordinal>=0,"Window stretch source was not in the explicit handle selection.","INVALID_ARGUMENT");
        Need(NativeStretchRead345.Token(before.Shape,before.Points,cancel)==edit.ExpectedStretchTokens[ordinal],"A window-stretch candidate changed; read every current complete native_stretch token.","GEOMETRY_CHANGED");
        var indices=NativeWindowStretchValidation387.Selected(edit,before.Points,cancel);var selected=new NativeStretchEdit345([handle],edit.ExpectedStretchTokens[ordinal],indices,edit.DisplacementWcs);
        _=NativeStretchRead345.Expected(before.Points,selected,cancel);return new(handle,curve,before,selected);
    }
    internal static void CheckSource(Entity source,string handle,NativeWindowStretchEdit387 edit,Database db,Transaction tr,Action cancel)
        =>_=Prepare(source,handle,edit,db,tr,cancel);
    private static Item[] Plan(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,Action cancel)
    {
        var edit=(NativeWindowStretchEdit387)args.Operations.Single();var result=new List<Item>();var budget=new CadContourBudget344(cancel);
        foreach(string handle in edit.Handles)
        {
            cancel();var source=(Entity)tr.GetObject(resolved.Sources[handle],OpenMode.ForRead);
            Need(source.OwnerId==resolved.Model&&CadBlockSpace.RequireWriteSpace(source.Database,tr,args.TargetSpace,cancel)==resolved.Model,"Every window-stretch source must belong to the requested active ordinary Model/Paper space.");
            var item=Prepare(source,handle,edit,source.Database,tr,cancel);
            // Budget only complete managed records, never DBObject/ObjectId.
            budget.Add(new{handle,item.Before,item.Edit.Indices,expected=NativeStretchRead345.Expected(item.Before.Points,item.Edit,cancel)});result.Add(item);
        }
        Need(result.Any(item=>item.Edit.Indices.Length>0),"The window selected no actual native stretch point in the supplied candidates.","STRETCH_POINT_NOT_FOUND");
        return result.ToArray();
    }
    private static void VerifyUnchanged(IEnumerable<Item> items,Transaction tr,Action cancel)
    {
        foreach(var item in items)
        {cancel();Need(SameState(item.Before,CadNativeStretch345.Capture(item.Source,tr,cancel)),"A window-stretch candidate changed before resident mutation.","NATIVE_STRETCH_MISMATCH");}
    }
    internal static void Preview(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,Action cancel)
    {
        var items=Plan(args,resolved,tr,cancel);
        foreach(var item in items.Where(item=>item.Edit.Indices.Length>0))
        {cancel();using var clone=CadContourBudget344.Detached(item.Source);_=CadNativeStretch345.Run((Curve)clone,item.Before,item.Edit,cancel);}
        VerifyUnchanged(items,tr,cancel);
    }
    private static void Output(ModifiedEntities result,IReadOnlyList<Item> items,List<string>? priorWarnings,Action cancel)
    {
        // Reserve the full possible notice set before the first native setter.
        // Int32 values below are serialized-width reservations, not count caps.
        var notices=new List<string>{WindowWarning,CadNativeStretch345.Warning};
        foreach(var item in items.Where(item=>item.Edit.Indices.Length>0&&item.Source is Polyline))
        {cancel();notices.Add("Source FFFFFFFFFFFFFFFF: NATIVE_STRETCH_ARC_OBSERVATION: Native stretch changed "+int.MaxValue+" stored bulge(s), first vertex "+int.MaxValue+". Selected/unselected native point requirements passed; inspect the incident arc shape against the engineering intent.");}
        if(priorWarnings!=null)notices.AddRange(priorWarnings);cancel();long bytes=JsonSerializer.SerializeToUtf8Bytes(new{data=result,warnings=notices},Wire.Json).LongLength;cancel();
        Need(bytes<=Wire.PayloadBudget-16384,"Complete window-stretch source refs/notices exceed the actual response-byte budget; no source was changed.","RESPONSE_LIMIT_EXCEEDED");
    }
    internal static ModifiedEntities Apply(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,WriteJob job,List<string>? warnings=null)
    {
        var items=Plan(args,resolved,tr,job.CheckCancellation);var moving=items.Where(item=>item.Edit.Indices.Length>0).ToArray();
        var refs=moving.Select(item=>new EntityRef(args.BridgeId,args.DocumentId,item.Handle)).ToArray();var result=new ModifiedEntities(args.OperationId,"committed",false,refs,[],[],[],"not_guaranteed");
        Output(result,items,warnings,job.CheckCancellation);VerifyUnchanged(items,tr,job.CheckCancellation);var notices=new List<string>{WindowWarning};
        foreach(var item in moving)
        {
            job.CheckCancellation();item.Source.UpgradeOpen();
            foreach(string notice in CadNativeStretch345.Run(item.Source,item.Before,item.Edit,job.CheckCancellation))
                notices.Add(notice==CadNativeStretch345.Warning?notice:"Source "+item.Handle+": "+notice);
        }
        foreach(var item in items)
        {
            job.CheckCancellation();var after=CadNativeStretch345.Capture(item.Source,tr,job.CheckCancellation);
            VerifyPoints(item,after,job.CheckCancellation);
            Need(item.Edit.Indices.Length==0?SameState(item.Before,after):Same(item.Before.Metadata,after.Metadata)&&Same(item.Before.Display,after.Display)&&BlockMoveGeometry.SameProperties(item.Before.Invariant,after.Invariant),
                "Window stretch changed an unselected candidate or source identity/owner/application data/retained appearance.","NATIVE_STRETCH_MISMATCH");
        }
        warnings?.AddRange(notices.Distinct(StringComparer.Ordinal));return result;
    }
}
