using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Dimensions;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Presentation is observed independently of solver health. A hidden sketch or
// an unresolved feature must remain inspectable; this route never enters an
// editor, creates a dimension, evaluates constraints or rebuilds the part.
internal static class SketchVisibilityState436
{
    internal sealed record Dimension(McObjectId Constraint,McObjectId Id,McDimension? Object,double? Visibility,string? Reason);
    internal sealed record Snapshot(PlanarSketch Sketch,ModelSketchPresentation436 Presentation,Dimension[] Dimensions,JsonElement Design);
    static string Key(McObjectId id)=>id.ToString();
    static string? Id(McObjectId id)=>id.IsNull?null:Key(id);
    internal static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    static bool? Visible(double? value)=>value.HasValue?value.Value>0:null;
    static object? Reference(IConstraintObjectInfo? value)
    {
        if(value==null)return null;
        var p=value.PointRefOnObject;
        double? Finite(double v)=>double.IsFinite(v)?v:null;
        return new{object_id=Id(value.ObjectId),subentity_id=Id(value.SubentId),segment=value.PolylineSegmentIndex,point=new[]{Finite(p.X),Finite(p.Y),Finite(p.Z)}};
    }
    internal static Snapshot Capture(Mc3dSolid body,PlanarSketch sketch,Action cancel)
    {
        cancel();bool editing=sketch.IsInEditMode;double native=sketch.DbEntity.Visibility;
        Need(double.IsFinite(native),"PRESENTATION_UNAVAILABLE","The native sketch visibility is nonfinite.");
        var owner=sketch.DbEntity.Document;
        Need(owner!=null&&!owner.ID.IsNull,"DOCUMENT_CONTEXT_MISMATCH","The actual sketch owner is unavailable.");
        var dimensions=new List<Dimension>();var relations=new List<object>();object? scope=null;string? unavailable=null;
        var budget=new ModelInventoryBudget(cancel);
        try
        {
            using var manager=NativeSketchMembers385.Manager(sketch);
            scope=new{document_id=Id(manager.DocumentId),scope_id=Id(manager.ScopeId),sketch_id=Id(manager.PlanarSketchId),network=manager.ConstraintsNetworkType.ToString()};
            var ids=manager.GetAllConstraints();Need(ids!=null,"CONSTRAINTS_UNAVAILABLE","The native relation inventory is unavailable.");
            foreach(var id in ids.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
            {
                cancel();using var info=manager.GetConstraintInfo(id);
                Need(info!=null,"CONSTRAINTS_UNAVAILABLE","A native relation is unavailable.");
                var row=new{id=Key(id),type=info.Type.ToString(),dimensional=info.IsDimensional,variable_id=Id(info.VarId),variable_name=info.VarName,
                    expression=info.VarExpression,parameter=double.IsFinite(info.Param)?(double?)info.Param:null,
                    dimension_id=Id(info.DimId),first=Reference(info.FirstObject),second=Reference(info.SecondObject),third=Reference(info.ThirdObject)};
                budget.Add(row);relations.Add(row);
                if(!info.IsDimensional)continue;
                McDimension? dimension=null;double? visibility=null;string? reason=null;
                if(info.DimId.IsNull)reason="The native relation exposes no current annotation ID in this sketch mode; the driving constraint remains present.";
                else
                {
                    try
                    {
                        dimension=info.DimId.GetObjectOfType<McDimension>();
                        if(dimension==null||dimension.ID!=info.DimId||dimension.DbEntity.Document?.ID!=manager.DocumentId)
                        {dimension=null;reason="The reported dimension does not resolve to its exact current annotation and manager document.";}
                        else
                        {
                            double observed=dimension.DbEntity.Visibility;
                            if(double.IsFinite(observed))visibility=observed;
                            else{dimension=null;reason="The native annotation visibility is nonfinite.";}
                        }
                    }
                    catch(OperationCanceledException){throw;}
                    catch(Exception error){dimension=null;reason="Native annotation observation failed: "+(error is DomainException d?d.Error.code:error.GetType().Name);}
                }
                dimensions.Add(new(id,info.DimId,dimension,visibility,reason));
            }
            // One visual object shared by multiple controls is not a proved
            // independent target. Report it instead of silently affecting peers.
            var shared=dimensions.Where(d=>!d.Id.IsNull).GroupBy(d=>d.Id).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
            for(int index=0;index<dimensions.Count;index++)if(shared.Contains(dimensions[index].Id))
                dimensions[index]=dimensions[index] with{Object=null,Visibility=null,Reason="The native annotation is shared by multiple dimensional relations."};
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error)
        {
            if(error is DomainException size&&size.Error.code is "RESPONSE_LIMIT_EXCEEDED" or "ENTITY_TOO_LARGE" or "ANALYSIS_LIMIT_EXCEEDED")throw;
            dimensions.Clear();relations.Clear();scope=null;
            unavailable="Current sketch annotation inventory unavailable: "+(error is DomainException d?d.Error.code:error.GetType().Name);
        }
        Need(sketch.IsInEditMode==editing,"DOCUMENT_CONTEXT_MISMATCH","The sketch editor mode changed during presentation observation.");
        var design=Wire.Element(new{body_id=Key(body.ID),sketch_id=Key(sketch.ID),owner_id=Key(owner.ID),in_edit_mode=editing,
            last_item_id=Id(body.LastPartItemID),history=ModelInventoryBudget.History(body,cancel).Select(Key).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray(),scope,relations,unavailable});
        var rows=dimensions.Select(d=>new ModelSketchDimensionPresentation436(Key(d.Constraint),Id(d.Id),d.Visibility,Visible(d.Visibility),d.Reason)).ToArray();
        bool complete=unavailable==null&&dimensions.All(d=>d.Object!=null&&d.Visibility.HasValue);
        bool? all=complete&&rows.Length>0&&rows.Select(d=>d.visible).Distinct().Count()==1?rows[0].visible:null;
        var token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{design,visibility=native,dimensions=rows},Wire.Json)));
        var presentation=new ModelSketchPresentation436(Key(sketch.ID),token,editing,native,Visible(native),all,complete,unavailable==null?rows:null,unavailable);
        budget.Add(presentation);
        return new(sketch,presentation,dimensions.ToArray(),design);
    }
    internal static ModelSketchPresentation436 Observe(Mc3dSolid body,PlanarSketch sketch,Action cancel)
    {
        try{return Capture(body,sketch,cancel).Presentation;}
        catch(OperationCanceledException){throw;}
        catch(Exception error)
        {
            return new(Key(sketch.ID),null,null,null,null,null,false,null,
                "Presentation observation unavailable: "+(error is DomainException d?d.Error.code:error.GetType().Name));
        }
    }
}
