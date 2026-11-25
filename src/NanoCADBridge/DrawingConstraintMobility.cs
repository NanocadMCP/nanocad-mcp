using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static partial class DrawingProfileWriter
{
    private static SketchMobilityReport Assess(IEnumerable<SketchConstraintCurve> selected, Network network,
        SketchConstraintSpec[]? proposed, HashSet<McObjectId>? replaced, Action cancel, IReadOnlyList<SketchMobilityMotion>? preserve=null)
    {
        var geometry = selected.ToArray();
        var active = network.Relations.Where(row => row.Spec != null).ToArray();
        SketchMobilityRelation RelationOf(Relation row) => new(row.Id.ToString(),row.Spec);
        if (replaced == null)
            return PlanDrawing(geometry,active.Select(RelationOf),proposed,network.Available,cancel,preserve);
        var before = PlanDrawing(geometry,active.Select(RelationOf),null,network.Available,cancel,null);
        var trial = PlanDrawing(geometry,active.Where(row=>!replaced.Contains(row.Id)).Select(RelationOf),
            proposed,network.Available,cancel,preserve,replacingDrivers:true);
        return CompareDriverState(before,trial);
    }

    internal static SketchMobilityReport CompareDriverState(SketchMobilityReport before,SketchMobilityReport trial)
    {
        var observed=before.components.ToDictionary(component=>component.members[0],StringComparer.Ordinal);
        var components=trial.components.Select(component=>component with
        {
            remaining_dof=observed.TryGetValue(component.members[0],out var previous)&&previous.members.SequenceEqual(component.members)
                ?previous.remaining_dof:null
        }).ToArray();
        return trial with
        {
            remaining_dof=before.remaining_dof,components=components,
            source="current_and_proposed_driver_values_in_the_observed_plane; candidate_decisions_compare_with_other_retained_relations"
        };
    }

    private static SketchMobilityReport PlanDrawing(IReadOnlyList<SketchConstraintCurve> geometry,
        IEnumerable<SketchMobilityRelation> known, SketchConstraintSpec[]? proposed, bool complete, Action cancel,
        IReadOnlyList<SketchMobilityMotion>? preserve, bool replacingDrivers=false)
    {
        var curves = geometry.ToDictionary(curve => curve.Id, StringComparer.OrdinalIgnoreCase);
        SketchConstraintSpec Canonical(SketchConstraintSpec spec) => DrawingConstraintOperands431.ForMobility(spec,curves);
        var retained = known.Select(row => row.Spec == null ? row : row with { Spec=Canonical(row.Spec) }).ToArray();
        return SketchMobility.Plan(geometry,retained,proposed?.Select(Canonical).ToArray(),complete,cancel:cancel,
            preserve:preserve,replacingDrivers:replacingDrivers);
    }

    internal static SketchMobilityReport InspectMobility(Arguments args, DocumentState state, Db.Transaction tx,
        McDocument sheet, Constraints2D? manager, IReadOnlyCollection<McObjectId> selected,
        Dictionary<McObjectId,McObjectId[]> operands, Func<string,string?> findHandle, bool complete, Action cancel)
    {
        var byMember = new Dictionary<McObjectId,List<McObjectId>>();
        var relations = new HashSet<McObjectId>();
        foreach (var (id, members) in operands)
        {
            cancel();
            if (members.Length == 0) { relations.Add(id); continue; }
            foreach (var member in members)
            {
                if (!byMember.TryGetValue(member, out var links)) byMember.Add(member, links = new());
                links.Add(id);
            }
        }
        var connected = selected.ToHashSet();
        var pending = new Queue<McObjectId>(connected);
        while (pending.Count != 0)
        {
            cancel();
            if (!byMember.TryGetValue(pending.Dequeue(), out var links)) continue;
            foreach (var id in links)
                if (relations.Add(id))
                    foreach (var member in operands[id])
                        if (connected.Add(member)) pending.Enqueue(member);
        }

        var frame = args.Workplane ?? DrawingWorkplane.XY;
        var sources = new Dictionary<string,Source>(StringComparer.OrdinalIgnoreCase);
        var geometry = new List<SketchConstraintCurve>();
        var names = new Dictionary<McObjectId,string>();
        bool Unreadable(DomainException e) => e.Error.code is "OUT_OF_SCOPE" or "CONSTRAINTS_UNAVAILABLE"
            or "OBJECT_IDENTITY_MISMATCH" or "INVALID_GEOMETRY" or "CONSTRAINT_SCOPE_MISMATCH"
            or "DRAWING_RELATION_MISMATCH" or "INVALID_ARGUMENT" or "UNSUPPORTED_ENTITY_TYPE"
            or "VARIABLES_UNAVAILABLE" or "CONSTRAINT_VALUE_UNAVAILABLE";
        foreach (var id in connected)
        {
            cancel();
            var handle = findHandle(id.ToString());
            string key = handle ?? id.ToString();
            names.Add(id, key);
            if (handle != null)
            {
                try
                {
                    var source = SourceOf(CadStructureWriter.Find(state.Document.Database,handle),state,tx,false,frame,cancel);
                    if (source.Native == id) { sources.Add(key,source); geometry.Add(source.Curve); continue; }
                }
                catch (DomainException error) when (Unreadable(error)) { }
            }
            geometry.Add(new(key, "unsupported"));
        }
        var known = new List<SketchMobilityRelation>();
        foreach (var id in relations)
        {
            cancel();
            var keys = operands[id].Select(member => names[member]).ToArray();
            SketchConstraintSpec? spec = null;
            if (keys.Length > 0 && keys.All(sources.ContainsKey))
            {
                using var info = manager!.GetConstraintInfo(id);
                if (info != null)
                {
                    try
                    {
                        double? value = null;
                        if (info.IsDimensional)
                        {
                            // The variable API supplies degrees for angular drivers. The
                            // constraint-manager scalar has no verified angular units here.
                            bool angular = info.Type == ConstraintType.DimAngular;
                            if (!info.VarId.IsNull && DrawingVariablesAccess.TryConstraintValue(sheet,info.VarId,
                                    info.VarName,info.VarExpression,angular,out double actual))
                                value = actual;
                            else if (!angular && manager.GetConstraintParamValue(id) is var linear && double.IsFinite(linear))
                                value = linear;
                        }
                        if (!info.IsDimensional || value.HasValue)
                            spec = DecodeConstraint(info,value,member => sources[names[member]],frame);
                    }
                    catch (DomainException error) when (Unreadable(error)) { }
                }
            }
            known.Add(new(id.ToString(), spec, keys));
        }
        var report=PlanDrawing(geometry,known,args.ProposedDrawingConstraints,complete,cancel,args.PreserveMotion);
        if(args.ProposedDrawingConstraints is {Length: >0} proposed && report.state!="rejected")
        {
            foreach(var spec in proposed)
            {
                var ids=new[]{spec.First,spec.Second,spec.Third}.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if(!ids.All(sources.ContainsKey))continue;
                try { DrawingProfileGeometry.Validate([spec],ids.Select(id=>sources[id].Curve).ToArray()); }
                catch(DomainException error) when(Unreadable(error))
                {
                    throw new DomainException(error.Error.code,error.Message,
                        details:new{constraint=spec,mobility=report,scope="drawing_add_requires_matching_current_geometry"});
                }
            }
        }
        return report;
    }

    internal static SketchConstraintSpec DecodeConstraint(ConstraintInfo info, double? value, Func<McObjectId,Source> Source,
        SketchPlaneSpec frame)
    {
        var refs = new[] { info.FirstObject, info.SecondObject, info.ThirdObject }
            .Where(reference => reference != null && !reference.ObjectId.IsNull).ToArray();
        string PointName(Source s,IConstraintObjectInfo info)
        {
            int? segment=DrawingConstraints431.Segment(s.Curve,info);var curve=DrawingConstraintOperands431.Resolve(s.Curve,segment);
            var p=info.PointRefOnObject;var xy=DrawingWorkplane.Local(frame,new(p.X,p.Y,p.Z));
            var roles=curve.Kind is "line" or "spline"?new[]{"start","end"}:curve.Kind=="arc"?new[]{"start","end","center"}:new[]{"center"};
            var matches=roles.Where(name=>DrawingProfileGeometry.Same(DrawingConstraintOperands431.Point(s.Curve,segment,name),xy)).ToArray();
            if(matches.Length==1)return matches[0];
            throw new DomainException("OUT_OF_SCOPE","Constraint reference is not a supported line endpoint or circle center.");
        }
        // Native endpoint/center constraints have persistent subentity IDs
        // even on exact Lines/Circles. Bind those IDs AND precise endpoint
        // coordinates; a nonnull ID does not imply a polyline segment.
        Need(refs.Length is >0 and <=3,"OUT_OF_SCOPE","Unexpected number of actual native relation operands.");
        var first=Source(info.FirstObject.ObjectId);Source? second=info.SecondObject==null||info.SecondObject.ObjectId.IsNull?null:Source(info.SecondObject.ObjectId);
        Source? third=info.ThirdObject==null||info.ThirdObject.ObjectId.IsNull?null:Source(info.ThirdObject.ObjectId);
        int? fs=DrawingConstraints431.Segment(first.Curve,info.FirstObject),ss=second==null?null:DrawingConstraints431.Segment(second.Curve,info.SecondObject!),ts=third==null?null:DrawingConstraints431.Segment(third.Curve,info.ThirdObject!);
        string kind=info.Type.ToString() switch
        {"GeomHorizontal"=>"horizontal","GeomVertical"=>"vertical","GeomParallel"=>"parallel","GeomPerpendicular"=>"perpendicular","GeomEqual"=>"equal",
            "GeomConcentric"=>"concentric","GeomCoincident"=>"coincident","GeomCollinear"=>"collinear","GeomTangent"=>"tangent","GeomSmooth"=>"smooth","GeomSymmetric"=>"symmetric","GeomFix"=>DrawingConstraints431.WholeObject(info.FirstObject)?"fix":"fix_point",
            "DimRadial"=>"radius","DimDiametrical"=>"diameter","DimAligned"=>first.Curve.Kind!="polyline"&&second?.Native==first.Native?"length":"distance","DimHorizontal"=>"distance_x","DimVertical"=>"distance_y","DimAngular"=>"angle",
            _=>throw new DomainException("OUT_OF_SCOPE","Selected component contains an unsupported native constraint type: "+info.Type)};
        string? fp=null,sp=null;
        if(kind is not "coincident" and not "smooth" and not "distance" and not "distance_x" and not "distance_y" and not "fix_point" and not "length")
            Need(refs.All(p=>p!.SubentId.IsNull||Source(p.ObjectId).Curve.Kind=="polyline"&&p.PolylineSegmentIndex>=0),"OUT_OF_SCOPE","Whole-curve relation unexpectedly references an unbound subentity.");
        if(kind is "coincident" or "smooth" or "distance" or "distance_x" or "distance_y" or "fix_point")fp=PointName(first,info.FirstObject);
        if(kind is "coincident" or "smooth" or "distance" or "distance_x" or "distance_y"){Need(second!=null,"CONSTRAINTS_UNAVAILABLE","Second source missing.");sp=PointName(second,info.SecondObject!);}
        if(kind=="length")Need(DrawingConstraintOperands431.Resolve(first.Curve,fs).Kind=="line"&&fs==DrawingConstraints431.Segment(first.Curve,info.SecondObject!)&&PointName(first,info.FirstObject)!=PointName(first,info.SecondObject!),"OUT_OF_SCOPE","Length must reference distinct endpoints of one line or straight Polyline segment.");
        Need(kind=="symmetric"?third!=null:third==null,"CONSTRAINT_SCOPE_MISMATCH","Unexpected third operand in an ordinary drawing relation.");
        var spec=new SketchConstraintSpec(kind,first.Curve.Id,kind=="length"?null:second?.Curve.Id,fp,sp,value,third?.Curve.Id,fs,kind=="length"?null:ss,ts);
        if (kind!="distance"||first.Curve.Kind!="polyline"||spec.Second!=spec.First) return spec;
        return DrawingConstraintOperands431.Canonical(spec,new Dictionary<string,SketchConstraintCurve>{{spec.First,first.Curve}});
    }
}
