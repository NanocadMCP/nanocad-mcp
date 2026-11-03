using System.Runtime.CompilerServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;
using MPlane=Multicad.Geometry.Plane3d;

namespace NanoCADBridge;

internal static class DocumentConstraintsReader
{
    internal static object Read(Arguments args,DocumentState state,Db.Transaction tx,Db.ObjectId modelId,string bridge,CancellationToken cancellation)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!loaded.Contains("mapimgd")||!loaded.Contains("MapiBaseTypes"))throw new DomainException("CAPABILITY_UNAVAILABLE","MultiCAD runtime must already be loaded.");
        if(state.Document.Database.CurrentSpaceId!=modelId)throw new DomainException("OUT_OF_SCOPE","Activate Model Space before inspecting drawing constraints.");
        foreach(var handle in args.Handles!)
        {
            cancellation.ThrowIfCancellationRequested();var id=CadStructureWriter.Find(state.Document.Database,handle);
            if(tx.GetObject(id,Db.OpenMode.ForRead) is not Db.Entity entity||entity.OwnerId!=modelId)
                throw new DomainException("OUT_OF_SCOPE","Constraint inspection targets existing top-level Model Space entities.");
        }
        return Native(args,state,tx,modelId,bridge,cancellation);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object Native(Arguments args,DocumentState state,Db.Transaction tx,Db.ObjectId modelId,string bridge,CancellationToken cancellation)
    {
        void Budget(){cancellation.ThrowIfCancellationRequested();}
        var doc=McDocument.ActiveDocument;var sheet=McDocumentsManager.GetActiveSheet();
        if(doc==null||sheet==null||!state.Document.IsNamedDrawing||!string.Equals(Path.GetFullPath(doc.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH","Matching named MultiCAD drawing required.");
        var sources=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var handle in args.Handles!)
        {
            var id=McObjectId.FromHandle(unchecked((long)Convert.ToUInt64(handle,16)),doc.ID);
            if(id.IsNull)throw new DomainException("OBJECT_IDENTITY_MISMATCH","Selected entity has no native constraint identity.");
            sources.Add(id.ToString(),handle);
        }
        // Unlike the public constructor used to CREATE a network, this explicit
        // read getter passes forceCreateReqData=false in the installed wrapper.
        using var plane=DrawingWorkplaneNative.Plane(args.Workplane??DrawingWorkplane.XY);using var manager=sheet.Get2dConstraintsManager(Constraint2dNetType.ACAD,sheet.ID,plane);Budget();
        if(manager==null)throw new DomainException("CONSTRAINTS_UNAVAILABLE","No readable ACAD network for the requested workplane; no empty-network claim is made.");
        if(manager.DocumentId!=sheet.ID||!manager.PlanarSketchId.IsNull||manager.ConstraintsNetworkType!=Constraint2dNetType.ACAD)
            throw new DomainException("CONSTRAINT_SCOPE_MISMATCH","Native manager resolved a different drawing or constraint network.");
        var ids=manager.GetAllConstraints();Budget();
        if(ids==null)throw new DomainException("CONSTRAINTS_UNAVAILABLE","Native constraint inventory is unavailable; not interpreted as empty.");
        string? Id(McObjectId id)=>id.IsNull?null:id.ToString();
        string? Text(string? value)=>value==null||value.Length<=8192?value:throw new DomainException("ENTITY_TOO_LARGE","Constraint text exceeds8192 characters.");
        // Resolve external endpoints to usable handles without expanding the
        // selected constraint set. This lets callers discover a connected
        // component before a dimension edit, rather than guessing opaque IDs.
        var referenceHandles=new Dictionary<string,string>(sources,StringComparer.OrdinalIgnoreCase);var referenceCapacity=new SerializedCapacity357(cancel:Budget);
        using var scan=((Db.BlockTableRecord)tx.GetObject(modelId,Db.OpenMode.ForRead)).Cast<Db.ObjectId>().GetEnumerator();bool exhausted=false;
        string? Handle(string identity)
        {
            if(referenceHandles.TryGetValue(identity,out var known))return known;
            while(!exhausted)
            {
                Budget();if(!scan.MoveNext()){exhausted=true;break;}
                var raw=scan.Current;var native=McObjectId.FromHandle(raw.Handle.Value,doc.ID);if(native.IsNull)continue;
                string key=native.ToString(),handle=raw.Handle.ToString();referenceCapacity.Add(new{object_id=key,handle});referenceHandles[key]=handle;if(key.Equals(identity,StringComparison.OrdinalIgnoreCase))return handle;
            }
            return null;
        }
        object? Reference(IConstraintObjectInfo? info)
        {
            if(info==null||info.ObjectId.IsNull)return null;string id=info.ObjectId.ToString();
            var point=info.PointRefOnObject;var xyz=new[]{point.X,point.Y,point.Z};bool finite=xyz.All(double.IsFinite);
            var handle=Handle(id);return new{object_id=id,source_ref=handle!=null?new EntityRef(bridge,state.Id,handle):null,
                subentity_id=Id(info.SubentId),polyline_segment_index=info.PolylineSegmentIndex,point=finite?xyz:null,
                point_unavailable_reason=finite?null:xyz.All(double.IsNaN)?"whole_object_or_unspecified_native_point":"non_finite_native_reference"};
        }
        var result=new List<object>();var resultCapacity=new SerializedCapacity357(cancel:Budget);
        var variableSources=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var variableConstraints=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(var id in ids.OrderBy(v=>v.ToString(),StringComparer.Ordinal))
        {
            Budget();using var info=manager.GetConstraintInfo(id);
            if(info==null)throw new DomainException("CONSTRAINTS_UNAVAILABLE","Constraint details are unavailable.");
            var participants=new[]{info.FirstObject,info.SecondObject,info.ThirdObject};
            if(args.IncludeVariables&&!info.VarId.IsNull)
            {
                var key=info.VarId.ToString();if(!variableSources.TryGetValue(key,out var handles)){variableSources[key]=handles=new(StringComparer.OrdinalIgnoreCase);variableConstraints[key]=new();}
                variableConstraints[key].Add(id.ToString());
                foreach(var p in participants.Where(p=>p!=null&&!p.ObjectId.IsNull)){var handle=Handle(p!.ObjectId.ToString());if(handle!=null)handles.Add(handle);}
            }
            if(!participants.Any(p=>p!=null&&sources.ContainsKey(p.ObjectId.ToString())))continue;
            var external=participants.Where(p=>p!=null&&!p.ObjectId.IsNull&&!sources.ContainsKey(p.ObjectId.ToString())).Select(p=>p!.ObjectId.ToString()).Distinct().ToArray();
            // ConstraintInfo.Param can be NaN even for a persisted driving radius.
            // The manager getter is the documented control value; expressions
            // are metadata and must never be parsed as a solver result.
            double parameter=info.IsDimensional?manager.GetConstraintParamValue(id):double.NaN;Budget();
            string parameterSource=info.IsDimensional?"unavailable":"not_applicable";
            if(double.IsFinite(parameter))parameterSource="constraint_manager";
            else if(info.IsDimensional&&!info.VarId.IsNull)
            {
                // Resolve the exact stored variable in the same drawing network.
                // Do not evaluate expressions or substitute an inferred measurement.
                if(DrawingVariablesAccess.TryConstraintValue(sheet,info.VarId,info.VarName,info.VarExpression,info.Type.ToString()=="DimAngular",out double value))
                {parameter=value;parameterSource="document_variable";}
                Budget();
            }
            var observation=new{mc_id=id.ToString(),type=info.Type.ToString(),is_geometrical=info.IsGeometrical,is_dimensional=info.IsDimensional,
                first=Reference(info.FirstObject),second=Reference(info.SecondObject),third=Reference(info.ThirdObject),
                parameter_value=double.IsFinite(parameter)?(double?)parameter:null,parameter_value_unit=info.Type.ToString()=="DimAngular"&&parameterSource=="document_variable"?"degrees":"native_constraint_units",parameter_value_source=parameterSource,
                variable_id=Id(info.VarId),variable_name=Text(info.VarName),variable_expression=Text(info.VarExpression),dimension_id=Id(info.DimId),
                references_outside_selection=external};resultCapacity.Add(observation);result.Add(observation);
        }
        object? inventory=null;
        if(args.IncludeVariables)
        {
            var variables=DrawingVariablesAccess.Read(sheet,Budget);
            inventory=new{scope="whole_ACAD_document_variable_network",dependency_direction="source_variable_to_direct_consumers",value_scope="stored_native_values_without_solver_evaluation",
                variables=variables.Select(v=>new{mc_id=v.Id.ToString(),name=v.Name,type=v.Type,expression=v.Expression,value=v.Value,value_available=v.Value.HasValue,
                    value_unit=v.Angular?(v.MetadataSource=="native_variable_manager"?"degrees":"unavailable_native_angular_units"):"native_variable_units",metadata_source=v.MetadataSource,host_object_id=Id(v.Host),downstream_variables=v.Downstream?.Select(x=>x.ToString()).ToArray(),downstream_objects=v.Objects?.Select(x=>x.ToString()).ToArray(),downstream_constraints=v.Constraints?.Select(x=>x.ToString()).ToArray(),
                    constraint_ids=variableConstraints.TryGetValue(v.Id.ToString(),out var ci)?ci.ToArray():Array.Empty<string>(),
                    source_refs=variableSources.TryGetValue(v.Id.ToString(),out var hs)?hs.Select(h=>new EntityRef(bridge,state.Id,h)).ToArray():Array.Empty<EntityRef>()}).ToArray()};
        }
        var frame=args.Workplane??DrawingWorkplane.XY;double[] Vector(Point3 p)=>new[]{p.X,p.Y,p.Z};
        Budget();var output=new{source_refs=args.Handles!.Select(h=>new EntityRef(bridge,state.Id,h)).ToArray(),network="ACAD",plane=args.Workplane==null?"WCS_XY":"explicit_workplane",workplane=new{origin=Vector(frame.Origin),x_axis=Vector(frame.XAxis),y_axis=Vector(frame.YAxis)},
            manager_document_id=sheet.ID.ToString(),scope_id=Id(manager.ScopeId),identity_scope="bridge_document_revision",
            reference_coordinate_system="native_constraint_reference",solver_status="not_evaluated",scanned_constraints=ids.Count,
            complete=true,completeness_scope="constraints_in_selected_workplane_network_referencing_any_selected_object; other planes and internal sketches are not inspected",constraints=result,variable_inventory=inventory};SerializedCapacity357.Check(output,Wire.PayloadBudget-4096,Budget);return output;
    }
}
