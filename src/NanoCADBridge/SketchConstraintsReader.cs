using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class SketchConstraintsReader
{
    public static object Read(Arguments args, DocumentState state, Transaction tx, ObjectId modelId, string instance, CancellationToken cancellation)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!loaded.Contains("mapimgd") || !loaded.Contains("MapiBaseTypes")) throw new DomainException("CAPABILITY_UNAVAILABLE","MultiCAD runtime is not already loaded.");
        long handle=unchecked((long)ulong.Parse(args.Handles![0],NumberStyles.HexNumber,CultureInfo.InvariantCulture));
        if(!state.Document.Database.TryGetObjectId(new Handle(handle),out var id) || id.IsNull || !id.IsValid) throw new DomainException("OBJECT_NOT_FOUND","Body not found.");
        if(id.IsErased || id.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED","Body was erased.");
        if(tx.GetObject(id,OpenMode.ForRead) is not Entity entity || entity.OwnerId!=modelId) throw new DomainException("OUT_OF_SCOPE","A top-level Model Space body is required.");
        return ReadMultiCad(args,state,handle,instance,cancellation);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object ReadMultiCad(Arguments args,DocumentState state,long handle,string instance,CancellationToken cancellation)
    {
        
        void Budget()=>cancellation.ThrowIfCancellationRequested();
        var active=Multicad.DatabaseServices.McDocument.ActiveDocument;
        if(active==null || !state.Document.IsNamedDrawing || !string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH","MultiCAD active document differs from the requested document.");
        var bodyId=Multicad.McObjectId.FromHandle(handle,active.ID);
        var body=bodyId.GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();
        if(body==null || body.ID!=bodyId) throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Source is not a matching Mc3dSolid.");
        var contents=body.GetPartContents(false);Budget();
        if(contents==null)throw new DomainException("ANALYSIS_LIMIT_EXCEEDED","Body history is unavailable.");
        var matches=contents.Where(x=>x.ToString()==args.ModelObjectId).ToArray();
        if(matches.Length!=1)throw new DomainException("OUT_OF_SCOPE","Sketch is not in this body's current history.");
        var sketchId=matches[0];var sketch=sketchId.GetObjectOfType<Multicad.Mc3D.PlanarSketch>();
        if(sketch==null || sketch.ID!=sketchId)throw new DomainException("UNSUPPORTED_ENTITY_TYPE","History item is not a PlanarSketch.");
        var owner=body.DbEntity.Document;
        if(sketch.DbEntity.Document?.ID!=owner.ID)throw new DomainException("CONSTRAINT_SCOPE_MISMATCH","The exact sketch and body owner differ.");
        using var manager=NativeSketchMembers385.Manager(sketch);
        Budget();
        if(manager==null)throw new DomainException("CAPABILITY_UNAVAILABLE","MCS constraints manager is unavailable.");
        var network=manager.ConstraintsNetworkType;var managerSketch=manager.PlanarSketchId;
        if(managerSketch!=sketchId)
            throw new DomainException("CONSTRAINT_SCOPE_MISMATCH","Native manager does not identify the requested sketch and document scope.",false,
                new {requested_sketch_id=sketchId.ToString(),actual_sketch_id=managerSketch.IsNull?null:managerSketch.ToString(),
                    actual_network=network.ToString(),actual_scope_id=manager.ScopeId.IsNull?null:manager.ScopeId.ToString(),
                    actual_document_id=manager.DocumentId.IsNull?null:manager.DocumentId.ToString()});
        var ids=manager.GetAllConstraints();Budget();
        if(ids==null)throw new DomainException("CONSTRAINTS_UNAVAILABLE","Native constraint inventory is null; no empty-inventory claim is made.");
        string? Id(Multicad.McObjectId id)=>id.IsNull?null:id.ToString();
        object? Reference(Multicad.DatabaseServices.Constraints.IConstraintObjectInfo? info)
        {
            if(info==null || info.ObjectId.IsNull)return null;
            var p=info.PointRefOnObject;var point=new[]{p.X,p.Y,p.Z};
            bool finite=point.All(double.IsFinite);
            string? reason=finite?null:point.All(double.IsNaN)?"whole_object_or_unspecified_native_point":"non_finite_native_reference";
            return new {object_id=info.ObjectId.ToString(),subentity_id=Id(info.SubentId),polyline_segment_index=info.PolylineSegmentIndex,
                point=finite?point:null,point_unavailable_reason=reason};
        }
        var constraints=new List<object>();
        foreach(var id in ids)
        {
            Budget();using var info=manager.GetConstraintInfo(id);
            if(info==null)throw new DomainException("CONSTRAINTS_UNAVAILABLE","Constraint details are unavailable.");
            double parameter=info.Param;
            constraints.Add(new {mc_id=id.ToString(),type=info.Type.ToString(),is_geometrical=info.IsGeometrical,is_dimensional=info.IsDimensional,
                first=Reference(info.FirstObject),second=Reference(info.SecondObject),third=Reference(info.ThirdObject),
                parameter_value=double.IsFinite(parameter)?(double?)parameter:null,parameter_value_unit="native_constraint_units",
                variable_id=Id(info.VarId),variable_name=Text(info.VarName),variable_expression=Text(info.VarExpression),dimension_id=Id(info.DimId)});
        }
        Budget();
        return new {source_ref=new EntityRef(instance,state.Id,args.Handles![0]),sketch_id=sketchId.ToString(),network=network.ToString(),
            manager_document_id=Id(manager.DocumentId),scope_id=Id(manager.ScopeId),identity_scope="bridge_document_revision",
            reference_coordinate_system="native_constraint_reference",solver_status="not_evaluated",constraints,
            presentation=SketchVisibilityState436.Observe(body,sketch,Budget)};
    }
    private static string? Text(string? text)=>text==null || text.Length<=8192?text:throw new DomainException("ENTITY_TOO_LARGE","Constraint text exceeds8192 characters.");
}
