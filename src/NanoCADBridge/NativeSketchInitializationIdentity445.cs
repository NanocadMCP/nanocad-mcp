using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.DatabaseServices.StandardObjects;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;

namespace NanoCADBridge;

// A one-call creation receipt, never a correspondence rule for existing
// objects. Geometry verifies intent only after the native ID delta is proved.
internal static class NativeSketchInitializationIdentity445
{
    internal sealed record Item(string Kind,JsonElement Geometry,JsonElement Storage);
    internal sealed record Inventory(McObjectId Sketch,McObjectId Owner,McObjectId Context,McObjectId Drawing,McObjectId Host,McObjectId Support,
        SketchPlaneSpec Plane,Dictionary<McObjectId,Item> Items,HashSet<McObjectId> Residents);
    internal sealed record ExitWitness(NativeSketchMembers385.Snapshot Inputs,NativeSketchCorrespondence410.Item[] Markers,NativeSketchCorrespondence410.Markers? Relations,Dictionary<McObjectId,JsonElement> Variables);
    static string Key(McObjectId id)=>id.ToString();
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string message,object? details=null)
    {if(!ok)throw new DomainException("SKETCH_BINDING_UNAVAILABLE",message,false,details);}
    static JsonElement Shape(G.EntityGeometry geometry,Action cancel)
    {
        cancel();
        if(geometry.GeometryType==G.EntityGeomType.kLine){using var value=geometry.LineSeg;return Wire.Element(SketchGeometryReader.Line(value));}
        if(geometry.GeometryType==G.EntityGeomType.kCircArc){using var value=geometry.CircArc;return Wire.Element(SketchGeometryReader.Arc(value));}
        if(geometry.GeometryType==G.EntityGeomType.kSpline){using var value=geometry.Nurb;return NativeSketchCurveGeometry445.Spline(value,cancel);}
        if(geometry.GeometryType==G.EntityGeomType.kEllipticArc){using var value=geometry.EllipArc;return NativeSketchCurveGeometry445.Ellipse(value,cancel);}
        if(NativeSketchMembers385.IsPoint(geometry))
        {var p=geometry.Point;Need(new[]{p.X,p.Y,p.Z}.All(double.IsFinite),"A native initialization point is nonfinite.");return Wire.Element(new{kind="point",point=new[]{p.X,p.Y,p.Z}});}
        throw new DomainException("SKETCH_GEOMETRY_UNAVAILABLE","A fresh Spline creation receipt requires complete supported native curve/point observations; no shape substitute is used.");
    }
    internal static Inventory Capture(PlanarSketch sketch,DocumentState state,Action cancel)
    {
        cancel();var owner=sketch.DbEntity.Document;var context=McDocumentsManager.GetActiveSheet();var active=Service.GetActiveEditSketch();
        Need(sketch.IsInEditMode&&owner!=null&&!owner.ID.IsNull&&context!=null&&!context.ID.IsNull&&Service.GetActiveEditSketchID()==sketch.ID&&active!=null&&active.ID==sketch.ID&&active.DbEntity.Document?.ID==owner.ID,
            "A creation receipt requires the exact current native sketch editor and owner.");
        var drawing=ModelProfileDiscovery348.DrawingDocument(state);
        var origin=sketch.TransformPointSketchToWorld(G.Point3d.Origin);var x=sketch.TransformPointSketchToWorld(new G.Point3d(1,0,0))-origin;var y=sketch.TransformPointSketchToWorld(new G.Point3d(0,1,0))-origin;
        var plane=new SketchPlaneSpec(new(origin.X,origin.Y,origin.Z),new(x.X,x.Y,x.Z),new(y.X,y.Y,y.Z));
        Need(new[]{origin.X,origin.Y,origin.Z,x.X,x.Y,x.Z,y.X,y.Y,y.Z}.All(double.IsFinite)&&Math.Abs(DrawingWorkplane.Dot(plane.XAxis,plane.XAxis)-1)<1e-9&&Math.Abs(DrawingWorkplane.Dot(plane.YAxis,plane.YAxis)-1)<1e-9&&Math.Abs(DrawingWorkplane.Dot(plane.XAxis,plane.YAxis))<1e-9,
            "The native creation frame must be finite and orthonormal.");
        var raw=sketch.GetAllItemsGeometry(false);Need(raw!=null,"The full pre/post native geometry-ID inventory is unavailable.");var items=new Dictionary<McObjectId,Item>();
        try
        {
            ModelInventoryBudget.Ids(raw.Keys,cancel);
            using var tx=state.Document.Database.TransactionManager.StartTransaction();
            foreach(var pair in raw)
            {
                cancel();Need(!pair.Key.IsNull&&pair.Value!=null,"A native creation inventory has an empty identity or geometry.");
                var shape=Shape(pair.Value,cancel);using var exact=sketch.GetItemGeometry(pair.Key,false);
                Need(exact!=null&&exact.GeometryType==pair.Value.GeometryType&&ModelParameterSnapshot.Equivalent(shape,Shape(exact,cancel)),"Native geometry map and exact WCS item getter disagree during creation observation.");
                var resident=pair.Key.GetObject();Need(resident==null||resident.ID==pair.Key,"A native geometry identity resolves a different resident object.");
                McDbObject? entity=resident is DbGeometry standard?standard.DbEntity:resident is McEntity native?native.DbObject:null;
                var document=entity?.Document;Need(resident==null||entity!=null&&document!=null&&!document.ID.IsNull,"An existing native input lacks observable resident ownership/metadata.");
                var metadata=resident==null?Wire.Element(new{scope="actual_global_resident_object_absence"}):ModelSketchLifecycleReader362.Metadata(pair.Key,entity!,document!,state,tx,cancel);
                object? display=resident is DbGeometry curve?new{layer=curve.DbEntity.Layer,visibility=curve.DbEntity.Visibility}:null;
                var storage=Wire.Element(new{resident_native_object_absent=resident==null,runtime_type=resident?.GetType().FullName,owner_document_id=document==null?null:Key(document.ID),display,metadata});
                items.Add(pair.Key,new(pair.Value.GeometryType.ToString(),shape,storage));
            }
        }
        finally{foreach(var value in raw.Values.Distinct(ReferenceEqualityComparer.Instance))if(value is IDisposable disposable)disposable.Dispose();}
        var residents=ModelProfileDiscovery348.Ids<DbGeometry>(context,cancel).ToHashSet();
        if(owner.ID!=context.ID)residents.UnionWith(ModelProfileDiscovery348.Ids<DbGeometry>(owner,cancel));
        return new(sketch.ID,owner.ID,context.ID,drawing.ID,sketch.HostSolidID,sketch.PlanarEntityID,plane,items,residents);
    }
    internal static McObjectId CreatedSpline(PlanarSketch sketch,Inventory before,Inventory after,NativeSketchPrimitive385 requested,McObjectId attempt,Action cancel)
    {
        cancel();Need(before.Sketch==after.Sketch&&before.Owner==after.Owner&&before.Context==after.Context&&before.Drawing==after.Drawing&&before.Host==after.Host&&before.Support==after.Support&&ModelParameterSnapshot.Equivalent(Wire.Element(before.Plane),Wire.Element(after.Plane)),
            "The native sketch/owner/frame/context changed during one Spline creation.");
        var added=after.Items.Keys.Except(before.Items.Keys).ToArray();var removed=before.Items.Keys.Except(after.Items.Keys).ToArray();
        Need(added.Length==1&&removed.Length==0,"One native AddGeometry did not produce one unambiguous new geometry identity; no existing object is reidentified.",
            new{stage="native_spline_creation_delta",requested_nonce=Key(attempt),new_count=added.Length,deleted_count=removed.Length,before_count=before.Items.Count,after_count=after.Items.Count});
        foreach(var pair in before.Items)
        {cancel();var current=after.Items[pair.Key];Need(pair.Value.Kind==current.Kind&&ModelParameterSnapshot.Equivalent(pair.Value.Geometry,current.Geometry)&&ModelParameterSnapshot.Equivalent(pair.Value.Storage,current.Storage),"A prior native geometry identity, full shape, owner or metadata changed during Spline creation.");}
        var id=added[0];var item=after.Items[id];
        Need(item.Kind==G.EntityGeomType.kSpline.ToString()&&!before.Residents.Contains(id)&&before.Residents.IsSubsetOf(after.Residents)&&after.Residents.Except(before.Residents).All(value=>value==id),
            "The new Spline delta identifies a preexisting resident source or changes unrelated resident geometry.");
        bool absent=item.Storage.GetProperty("resident_native_object_absent").GetBoolean();
        Need(absent||item.Storage.GetProperty("owner_document_id").GetString()==Key(after.Context),"The new resident Spline is outside the actual editor's owning document.");
        Need(NativeSketchCurveGeometry445.Requested(requested,item.Geometry,after.Plane,cancel),"The new actual native Spline does not match its full requested WCS definition.");
        var projections=sketch.GetContainingObjects(false,true,false);Need(projections!=null&&!projections.Contains(id),"A newly observed Spline is a projection or its role is unavailable.");
        foreach(var peerId in ModelProfileDiscovery348.Ids<PlanarSketch>(sketch.DbEntity.Document,cancel))
        {
            cancel();if(peerId==sketch.ID)continue;var peer=peerId.GetObjectOfType<PlanarSketch>();Need(peer!=null&&peer.ID==peerId&&peer.DbEntity.Document?.ID==after.Owner,"A new Spline's peer ownership inventory is unavailable.");
            var raw=peer.GetAllItemsGeometry(false);Need(raw!=null,"A new Spline's peer geometry inventory is unavailable.");
            try{Need(!raw.ContainsKey(id),"The new sketch item is already shared by another actual native sketch.");}
            finally{foreach(var value in raw.Values.Distinct(ReferenceEqualityComparer.Instance))if(value is IDisposable disposable)disposable.Dispose();}
        }
        return id;
    }
    static Dictionary<long,int> MarkerCounts(PlanarSketch sketch,Action cancel)
    {
        cancel();var raw=sketch.GetAllItemsGeometry(false);Need(raw!=null,"The actual native marker inventory is unavailable.");
        try{return raw.Values.Where(value=>value!=null&&value.GSMarker!=-1).GroupBy(value=>(long)value.GSMarker).ToDictionary(group=>group.Key,group=>group.Count());}
        finally{foreach(var value in raw.Values.Distinct(ReferenceEqualityComparer.Instance))if(value is IDisposable disposable)disposable.Dispose();}
    }
    internal static NativeSketchRelations410.Baseline CapturePriorBaseline(Constraints2D manager,NativeSketchMembers385.Snapshot inputs,IReadOnlySet<string> actualRetainedIds,Action cancel)
    {
        cancel();var inventory=manager.GetAllConstraints();Need(inventory!=null,"The actual retained baseline relation inventory is unavailable.");ModelInventoryBudget.Ids(inventory,cancel);
        var selected=inventory.Where(id=>actualRetainedIds.Contains(Key(id))).ToArray();
        Need(selected.Length==actualRetainedIds.Count&&selected.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(actualRetainedIds),"A retained baseline relation disappeared before its typed native helper capture.");
        // The manager overload preserves exact relation/dimension/variable IDs
        // as well as construction anchors. The JSON validation overload alone
        // does not populate those native helper identities.
        return NativeSketchRelations410.Capture(manager,inputs,cancel,selected);
    }
    internal static ExitWitness CaptureExit(NativeSketchMembers385.Snapshot inputs,JsonElement? priorBaselineRelations,Action cancel)
    {
        var sketch=inputs.Sketch;Need(sketch.IsInEditMode&&Service.GetActiveEditSketchID()==sketch.ID&&McDocumentsManager.GetActiveSheet()?.ID.ToString()==inputs.Stored.GetProperty("member_context_document_id").GetString(),
            "Initialization exit witnesses may only be captured in their exact current editor.");
        var counts=MarkerCounts(sketch,cancel);var markers=new List<NativeSketchCorrespondence410.Item>();
        foreach(var member in inputs.Members.Where(member=>member.Owned&&!member.ReferenceOnly&&member.Segment==null&&member.Id==member.ObjectId&&member.NativeType=="kSpline"))
        {
            cancel();using var actual=sketch.GetItemGeometry(member.Id,false);Need(actual!=null&&actual.GeometryType==G.EntityGeomType.kSpline&&NativeSketchMembership445.Member(sketch,member.Id,actual,cancel),"An initialization Spline lost its actual native membership before editor exit.");
            long marker=actual.GSMarker;
            if(marker!=-1&&counts.TryGetValue(marker,out int count)&&count==1&&sketch.GetSketchEntityByGsMarker(marker)==member.Id)
                markers.Add(new(member.Id,marker,member.NativeType,false));
        }
        NativeSketchCorrespondence410.Markers? complete=null;var variables=new Dictionary<McObjectId,JsonElement>();
        if(priorBaselineRelations.HasValue)
        {
            // Caller selected these actual rows through Retained(baseline,...).
            // Newly submitted unpublished constraints are resolved by specs
            // after exit; they are not prior user/control identity witnesses.
            NativeSketchRelations410.Capture(priorBaselineRelations.Value,inputs,cancel);
            complete=new(inputs,markers.ToArray(),priorBaselineRelations.Value,null,Wire.Element(new{scope="fresh_initialization_exit; retained_prior_baseline_only; no_invented_optional_origin_alias"}),[]);
            foreach(var variable in priorBaselineRelations.Value.GetProperty("constraints").EnumerateArray().Where(row=>row.GetProperty("variable").ValueKind==JsonValueKind.String).Select(row=>row.GetProperty("variable").GetString()!).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancel();var id=inputs.Helpers.Single(value=>Key(value).Equals(variable,StringComparison.OrdinalIgnoreCase));variables.Add(id,NativeSketchCorrespondence410.OwnerVariable(inputs,id,cancel));
            }
        }
        return new(inputs,markers.ToArray(),complete,variables);
    }
    internal static void VerifyNewDriver(Constraints2D manager,McObjectId relation,ConstraintInfo info,NativeSketchMembers385.Snapshot inputs,SketchConstraintSpec requested,Action cancel)
    {
        cancel();Need(requested.Value.HasValue&&info.IsDimensional&&!info.VarId.IsNull&&manager.GetConstraintParamId(relation)==info.VarId&&ModelParameterSnapshot.Near(manager.GetConstraintParamValue(relation),requested.Value.Value),
            "The new persistent dimension has no actual requested native control value/binding.");
        NativeSketchDimensionBinding411.Prove(manager,relation,info,inputs,cancel);
        var inventory=manager.GetAllConstraints();Need(inventory!=null,"The new dimensional relation inventory is unavailable.");int shared=0;
        foreach(var id in inventory)
        {cancel();using var row=manager.GetConstraintInfo(id);Need(row!=null,"A current native relation is unavailable while proving its new driver.");if(row.VarId==info.VarId)shared++;}
        Need(shared==1,"A newly created dimension variable does not bind one unique actual relation.");
        var owner=inputs.Sketch.DbEntity.Document;using var variables=owner.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(variables!=null&&variables.DocumentId==owner.ID&&variables.ConstraintsNetworkType==Constraint2dNetType.MCS,"The new dimension's actual owner MCS variable manager is unavailable.");
        var ids=variables.GetAllVariables();Need(ids!=null&&ids.Count(id=>id==info.VarId)==1,"The new dimension variable is not unique in its actual owning MCS network.");
        var actual=variables.GetVariableInfo(info.VarId);Need(actual!=null,"The new persistent dimensional variable metadata is unavailable.");
        var flags=actual.Type.ToString().Split(',',StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        if(flags.Contains("Reference"))throw new DomainException("REFERENCE_DIMENSION_READ_ONLY","The native solver created a reference measurement rather than the requested driving dimension.");
        Need(flags.SetEquals(new[]{"Dim2DConstraint"})&&variables.GetVariableHostObjectId(info.VarId)==inputs.Sketch.ID&&variables.IsAngularValue(info.VarId)==(requested.Kind=="angle"),
            "The new dimension has an unsupported type/unit or a different actual native sketch host.");
        Need(variables.GetVariableValue(info.VarId,out double value)&&double.IsFinite(value)&&ModelParameterSnapshot.Near(value,requested.Value.Value)&&
            double.TryParse(actual.Expression,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double literal)&&ModelParameterSnapshot.Near(literal,requested.Value.Value),
            "The new persistent dimensional variable does not evaluate to its requested literal control.");
    }
    internal static NativeSketchCorrespondence410.Mapping ResolveExit(ExitWitness witness,NativeSketchMembers385.Snapshot current,JsonElement? relations,Action cancel,Constraints2D? manager=null)
    {
        var before=witness.Inputs;Need(!current.Sketch.IsInEditMode&&before.Sketch.ID==current.Sketch.ID&&before.Stored.GetProperty("owner_document_id").GetString()==current.Stored.GetProperty("owner_document_id").GetString()&&ModelParameterSnapshot.Equivalent(Wire.Element(before.Plane),Wire.Element(current.Plane)),
            "Initialization editor exit changed its selected native sketch, owner or frame.");
        var changed=before.Members.Where(member=>!current.Members.Any(actual=>actual.Id==member.Id&&actual.ObjectId==member.ObjectId&&actual.Segment==member.Segment)).ToArray();
        var counts=changed.Length==0?new Dictionary<long,int>():MarkerCounts(current.Sketch,cancel);var selected=new List<NativeSketchCorrespondence410.Item>();
        foreach(var member in changed)
        {
            cancel();Need(member.Owned&&!member.ReferenceOnly&&member.NativeType=="kSpline"&&member.Id==member.ObjectId&&member.Segment==null,"A non-Spline or immutable input changed identity during initialization exit.");
            var markers=witness.Markers.Where(item=>item.Original==member.Id).ToArray();Need(markers.Length==1&&counts.TryGetValue(markers[0].Marker,out int count)&&count==1,"A changed Spline identity has no unique observed native GS marker; no geometric or ordinal alias is inferred.");selected.Add(markers[0]);
        }
        if(witness.Relations!=null)
        {
            Need(relations.HasValue,"A complete pre-exit relation inventory cannot be replaced by an unavailable post-exit network.");
            var mapping=NativeSketchCorrespondence410.Resolve(witness.Relations with{Items=selected.ToArray()},current,relations.Value,cancel,"initialization_after_exit",preserveGeometry:true,observedManager:manager);
            foreach(var variable in witness.Variables)
            {
                cancel();var actual=NativeSketchCorrespondence410.OwnerVariable(current,mapping.Map(variable.Key),cancel);
                Need(ModelParameterSnapshot.Equivalent(NativeSketchCorrespondence410.Identities(variable.Value,mapping,cancel),actual),"Initialization exit changed a native dimension variable's owner, host, type, name, expression or evaluated value.");
            }
            return mapping;
        }
        // Geometry-only creation with an unavailable network remains explicitly
        // unavailable. Prove member identity/geometry, never fabricate relations.
        var ids=new Dictionary<McObjectId,McObjectId>();var used=new HashSet<McObjectId>();
        foreach(var member in before.Members)
        {
            cancel();var marker=selected.SingleOrDefault(item=>item.Original==member.ObjectId);var target=marker==null?member.ObjectId:current.Sketch.GetSketchEntityByGsMarker(marker.Marker);
            var matches=current.Members.Where(actual=>actual.ObjectId==target&&actual.Segment==member.Segment&&actual.NativeType==member.NativeType&&actual.Owned==member.Owned&&actual.ReferenceOnly==member.ReferenceOnly).ToArray();
            Need(!target.IsNull&&matches.Length==1&&used.Add(matches[0].Id)&&DrawingProfileGeometry.Same(member.Curve,matches[0].Curve),"Geometry-only initialization exit lost its exact native identity/role or full curve geometry.");
            if(marker!=null){using var actual=current.Sketch.GetItemGeometry(target,false);Need(actual!=null&&actual.GSMarker==marker.Marker&&NativeSketchMembership445.Member(current.Sketch,target,actual,cancel),"The initialization exit marker lacks its exact native reverse item.");}
            ids[member.Id]=matches[0].Id;ids[member.ObjectId]=target;
        }
        Need(before.Members.Length==current.Members.Length&&used.SetEquals(current.Members.Select(member=>member.Id)),"Geometry-only initialization exit is not a complete native member bijection.");
        foreach(var point in before.Construction)Need(current.Construction.Any(actual=>actual.Id==point.Id&&ModelParameterSnapshot.Equivalent(point.Geometry,actual.Geometry)),"A construction point changed without a witnessed relation network.");
        return new(ids,Wire.Element(new{phase="geometry_only_initialization_after_exit",identity_basis="same_exact_native_ID_or_unique_observed_GS_marker; no_relation_preservation_claim"}));
    }
}
