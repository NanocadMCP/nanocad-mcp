using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;
using MPlane=Multicad.Geometry.Plane3d;
using MPoint=Multicad.Geometry.Point3d;

namespace NanoCADBridge;

// Ordinary ACAD networks and explicitly projected source contours. The internal
// MCS sketch network is deliberately a different route with different evidence.
internal static partial class DrawingProfileWriter
{
    private static void Need([DoesNotReturnIf(false)] bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    internal sealed record Source(Db.ObjectId Id,McObjectId Native,SketchConstraintCurve Curve,string Layer,SketchPlaneSpec Workplane,DrawingConstraints431.Parent? Parent=null);
    private sealed record Relation(McObjectId Id,string Type,SketchConstraintSpec? Spec,McObjectId Variable,string? Name,string? Expression,double? Value,string References);
    private sealed record Network(bool Available,Dictionary<string,Source> Sources,Relation[] Relations);
    private sealed record Binding(McDocument Sheet,Dictionary<string,Source> Selected,Network Network);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is DrawingProfileEdit);
    private static Db.ObjectId Model(DocumentState state,Db.Transaction tx)=>((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
    private static void Layer(Db.Entity entity,Db.Transaction tx)
    {var layer=(Db.LayerTableRecord)tx.GetObject(entity.LayerId,Db.OpenMode.ForRead);Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","Selected source or consuming body layer is unavailable.");}
    internal static Source SourceOf(Db.ObjectId id,DocumentState state,Db.Transaction tx,bool editable,SketchPlaneSpec? workplane=null,Action? cancellation=null)
    {
        var entity=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Entity;
        Need(entity!=null&&entity.OwnerId==Model(state,tx),"OUT_OF_SCOPE","Use top-level Model Space sources.");if(editable)Layer(entity,tx);
        string handle=id.Handle.ToString();SketchConstraintCurve curve;var frame=workplane??DrawingWorkplane.XY;var normal=DrawingWorkplane.Normal(frame);
        Point2 XY(Teigha.Geometry.Point3d p)
        {return DrawingWorkplane.Local(frame,new(p.X,p.Y,p.Z));}
        if(entity.GetType()==typeof(Db.Line))
        {
            var line=(Db.Line)entity;Need(Math.Abs(line.Thickness)<1e-9,"OUT_OF_SCOPE","Use zero-thickness Lines.");
            curve=new(handle,"line",XY(line.StartPoint),XY(line.EndPoint));Need(DrawingProfileGeometry.Distance(curve.Start!,curve.End!)>=.001,"INVALID_GEOMETRY","Source line is degenerate.");
        }
        else if(entity.GetType()==typeof(Db.Arc))
        {
            var arc=(Db.Arc)entity;
            Need(Math.Abs(arc.Thickness)<1e-9&&(arc.Normal-new Teigha.Geometry.Vector3d(normal.X,normal.Y,normal.Z)).Length<1e-9,"OUT_OF_SCOPE","Use the workplane normal, zero-thickness Arc.");
            double sweep=arc.TotalAngle;
            curve=new(handle,"arc",XY(arc.StartPoint),XY(arc.EndPoint),XY(arc.Center),arc.Radius,sweep);DrawingArcContours.ValidateArc(curve);
        }
        else if(entity.GetType()==typeof(Db.Polyline))
        {
            var polyline=(Db.Polyline)entity;
            curve=new(handle,"polyline",Polyline:CadPolylineWorkplane431.Capture(polyline,frame,cancellation??(()=>{})));
        }
        else if(entity.GetType()==typeof(Db.Spline))
            curve=DrawingSplineConstraints445.Capture(handle,(Db.Spline)entity,frame,cancellation??(()=>{}));
        else
        {
            Need(entity.GetType()==typeof(Db.Circle),"OUT_OF_SCOPE","This drawing network route supports exact native Lines, Arcs, Circles, lightweight Polylines and Splines.");var circle=(Db.Circle)entity;
            Need(Math.Abs(circle.Thickness)<1e-9&&(circle.Normal-new Teigha.Geometry.Vector3d(normal.X,normal.Y,normal.Z)).Length<1e-9&&circle.Radius>=.001&&circle.Radius<=1e6,"OUT_OF_SCOPE","Use the workplane normal, zero-thickness Circle with bounded radius.");
            curve=new(handle,"circle",Center:XY(circle.Center),Radius:circle.Radius);
        }
        var native=McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID);Need(!native.IsNull,"OBJECT_IDENTITY_MISMATCH","Source native identity is unavailable.");
        return new(id,native,curve,entity.Layer,frame,entity is Db.Polyline parent?new(new(parent.Normal.X,parent.Normal.Y,parent.Normal.Z),parent.Elevation,parent.Thickness,parent.Closed,parent.NumberOfVertices):null);
    }
    private static Constraints2D? Manager(McDocument sheet,SketchPlaneSpec? workplane=null)
    {
        using var plane=DrawingWorkplaneNative.Plane(workplane??DrawingWorkplane.XY);var manager=sheet.Get2dConstraintsManager(Constraint2dNetType.ACAD,sheet.ID,plane);
        if(manager!=null&&(manager.DocumentId!=sheet.ID||!manager.PlanarSketchId.IsNull||manager.ConstraintsNetworkType!=Constraint2dNetType.ACAD))
        {manager.Dispose();throw new DomainException("CONSTRAINT_SCOPE_MISMATCH","Expected ordinary ACAD network for the selected workplane.");}
        return manager;
    }
    private static Network ReadNetwork(McDocument sheet,Dictionary<string,Source> selected,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var frame=selected.Values.First().Workplane;using var manager=Manager(sheet,frame);var ids=manager?.GetAllConstraints();
        if(ids==null)return new(false,new(selected,StringComparer.OrdinalIgnoreCase),Array.Empty<Relation>());
        ModelInventoryBudget.Ids(ids,cancel);var inventoryBudget=new ModelInventoryBudget(cancel);
        var index=new Dictionary<McObjectId,Db.ObjectId>();
        foreach(Db.ObjectId id in (Db.BlockTableRecord)tx.GetObject(Model(state,tx),Db.OpenMode.ForRead))
        {cancel();index[McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)]=id;}
        var sources=new Dictionary<string,Source>(selected,StringComparer.OrdinalIgnoreCase);var relations=new List<Relation>();
        Source Source(McObjectId id)
        {
            Need(index.TryGetValue(id,out var raw),"OUT_OF_SCOPE","Network references a source outside top-level Model Space.");string h=raw.Handle.ToString();
            if(!sources.TryGetValue(h,out var s)){s=SourceOf(raw,state,tx,false,frame,cancel);inventoryBudget.Add(s.Curve);sources.Add(h,s);}return s;
        }
        string PointName(Source s,IConstraintObjectInfo info)
        {
            int? segment=DrawingConstraints431.Segment(s.Curve,info);var curve=DrawingConstraintOperands431.Resolve(s.Curve,segment);
            var p=info.PointRefOnObject;var xy=DrawingWorkplane.Local(frame,new(p.X,p.Y,p.Z));
            var roles=curve.Kind is "line" or "spline"?new[]{"start","end"}:curve.Kind=="arc"?new[]{"start","end","center"}:new[]{"center"};
            var matches=roles.Where(name=>DrawingProfileGeometry.Same(DrawingConstraintOperands431.Point(s.Curve,segment,name),xy)).ToArray();
            if(matches.Length==1)return matches[0];
            throw new DomainException("OUT_OF_SCOPE","Constraint reference is not a supported line endpoint or circle center.");
        }
        foreach(var id in ids.OrderBy(v=>v.ToString(),StringComparer.Ordinal))
        {
            cancel();using var info=manager!.GetConstraintInfo(id);Need(info!=null,"CONSTRAINTS_UNAVAILABLE","Constraint details unavailable.");
            var refs=new[]{info.FirstObject,info.SecondObject,info.ThirdObject}.Where(p=>p!=null&&!p.ObjectId.IsNull).ToArray();
            var participants=refs.Select(p=>Source(p!.ObjectId)).ToArray();bool touched=participants.Any(p=>selected.ContainsKey(p.Curve.Id));
            if(touched&&participants.Any(p=>!selected.ContainsKey(p.Curve.Id)))throw new DomainException("INCOMPLETE_CONSTRAINT_SELECTION","Include every source connected by constraints to the selected sources.",false,
                new{missing_handles=participants.Where(p=>!selected.ContainsKey(p.Curve.Id)).Select(p=>p.Curve.Id).Distinct().ToArray()});
            string? name=null,expression=null;double? value=null;
            if(info.IsDimensional)
            {
                Need(!info.VarId.IsNull,"CONSTRAINT_VALUE_UNAVAILABLE","Dimension has no native variable.");
                double actual=double.NaN;Need(DrawingVariablesAccess.TryConstraintValue(sheet,info.VarId,info.VarName,info.VarExpression,info.Type.ToString()=="DimAngular",out actual),"CONSTRAINT_VALUE_UNAVAILABLE","Exact native dimensional value unavailable.");
                value=actual;name=info.VarName;expression=info.VarExpression;
            }
            SketchConstraintSpec? spec=null;
            if(touched)
            {
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
                spec=new(kind,first.Curve.Id,kind=="length"?null:second?.Curve.Id,fp,sp,value,third?.Curve.Id,fs,kind=="length"?null:ss,ts);
                spec=DrawingConstraintOperands431.Canonical(spec,sources.ToDictionary(row=>row.Key,row=>row.Value.Curve,StringComparer.OrdinalIgnoreCase));
            }
            string references=string.Join("|",refs.Select(p=>p!.ObjectId+":"+p.SubentId+":"+p.PolylineSegmentIndex));
            inventoryBudget.Add(new{id=id.ToString(),type=info.Type.ToString(),variable=info.VarId.ToString(),name,expression,value,references,spec});
            relations.Add(new(id,info.Type.ToString(),spec,info.VarId,name,expression,value,references));
        }
        return new(true,sources,relations.ToArray());
    }
    private static Binding Resolve(DrawingProfileEdit edit,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var doc=McDocument.ActiveDocument;var sheet=McDocumentsManager.GetActiveSheet();
        Need(doc!=null&&sheet!=null&&state.Document.IsNamedDrawing&&string.Equals(Path.GetFullPath(doc.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),"DOCUMENT_CONTEXT_MISMATCH","Matching named drawing required.");
        Need(state.Document.Database.CurrentSpaceId==Model(state,tx),"OUT_OF_SCOPE","Activate Model Space.");
        var selected=edit.Handles.Select(h=>SourceOf(CadStructureWriter.Find(state.Document.Database,h),state,tx,true,edit.Workplane,cancel)).ToDictionary(s=>s.Curve.Id,StringComparer.OrdinalIgnoreCase);
        if(edit.Action is not "extrude_drawing_profile" and not "revolve_drawing_profile"&&selected.Values.Any(s=>s.Curve.Kind=="arc"||s.Curve.Polyline?.Vertices.Any(v=>v.Bulge!=0)==true))
            DrawingWorkplaneNative.RequireArcSolverFrame(sheet,selected.Values.First().Workplane,state,tx);
        if(edit.Action=="add_drawing_constraints"&&edit.Constraints!.Any(c=>c.Kind=="angle")&&
            selected.Values.First().Workplane!=DrawingWorkplane.XY&&!DrawingVariablesAccess.ServiceExists(sheet))
            throw new DomainException("OUT_OF_SCOPE","Stored angular units are not verified without an existing native XY variable service. This angular constraint was refused before writing; linear dimensions and User formulas remain available in the requested plane.");
        if(edit.Action=="revolve_drawing_profile")
        {
            var axis=SourceOf(CadStructureWriter.Find(state.Document.Database,edit.AxisHandle!),state,tx,true,edit.Workplane);
            Need(axis.Curve.Kind=="line"&&!selected.ContainsKey(axis.Curve.Id),"INVALID_ARGUMENT","Revolution needs a separate native Line axis.");
            var curves=selected.Values.Select(s=>s.Curve).ToArray();
            if(curves.Any(c=>c.Kind=="arc"))ModelPolycurve.FromCurves(curves,cancel);
            else DrawingProfileGeometry.Profile(curves,cancel);
            selected.Add(axis.Curve.Id,axis);
        }
        var network=edit.Action is "extrude_drawing_profile" or "revolve_drawing_profile"?new Network(false,selected,Array.Empty<Relation>()):ReadNetwork(sheet,selected,state,tx,cancel);
        if(edit.Action=="add_drawing_constraints")
        {
            foreach(var spec in edit.Constraints!)foreach(var reference in new[]{(spec.First,spec.FirstPoint),(spec.Second,spec.SecondPoint)})
                if(reference.Item1!=null&&reference.Item2 is "start" or "end"&&selected[reference.Item1].Curve is {Kind:"spline",Start:{} first,End:{} last})
                    Need(!DrawingProfileGeometry.Same(first,last),"CONSTRAINT_SCOPE_MISMATCH","This ordinary drawing Spline has coincident start/end positions whose pick roles cannot be uniquely observed by this adapter; no relation has been written.");
            DrawingProfileGeometry.Validate(edit.Constraints!,selected.Values.Select(s=>s.Curve).ToArray());
            foreach(var spec in edit.Constraints!)Need(!network.Relations.Any(r=>r.Spec is {} old&&SameRelation(old,spec,selected)),"DUPLICATE_CONSTRAINT","A matching relation already exists.");
        }
        else if(edit.Action=="set_drawing_dimension")
        {
            Need(network.Available,"CONSTRAINTS_UNAVAILABLE","Read a complete existing network before editing.");var relation=FindDimension(edit,network);
            var variables=DrawingVariablesAccess.Read(sheet,cancel);var ids=variables.Select(v=>v.Id).ToArray();
            Need(!string.IsNullOrWhiteSpace(relation.Name),"VARIABLES_UNAVAILABLE","Native variable name is unavailable.");
            var variableBudget=new ModelInventoryBudget(cancel);foreach(var variable in variables)variableBudget.Add(new{id=variable.Id.ToString(),variable.Name,variable.Expression});
            var reference=new Regex(@"(?<![\p{L}\p{N}_])"+Regex.Escape(relation.Name)+@"(?![\p{L}\p{N}_])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
            foreach(var id in ids.Where(v=>v!=relation.Variable))
            {cancel();var variable=variables.Single(v=>v.Id==id);Need(variable.Expression!=null&&variable.Expression.Length<=8192,"VARIABLES_UNAVAILABLE","Native expression unavailable.");Need(!reference.IsMatch(variable.Expression),"OUT_OF_SCOPE","Another drawing variable depends on this dimension; formula propagation is outside this numeric edit scope.");}
        }
        else if(edit.Action=="extrude_drawing_profile")
        {
            var curves=selected.Values.Select(s=>s.Curve).ToArray();
            if(curves.Any(c=>c.Kind=="arc"))ModelPolycurve.FromCurves(curves,cancel);
            else DrawingProfileGeometry.Profile(curves,cancel);
        }
        var binding=new Binding(sheet,selected,network);
        if(IsRemoval(edit))PrepareRemoval(edit,binding,cancel);
        if(DrawingVariablePlanning.IsAction(edit.Action))PrepareVariables(edit,binding,state,cancel);
        return binding;
    }
    private static bool SameRelation(SketchConstraintSpec a,SketchConstraintSpec b,Dictionary<string,Source> sources)
    {
        var curves=sources.ToDictionary(row=>row.Key,row=>row.Value.Curve,StringComparer.OrdinalIgnoreCase);
        a=DrawingConstraintOperands431.Canonical(a,curves);b=DrawingConstraintOperands431.Canonical(b,curves);
        bool Same(string? first,int? segment,string? point,string? second,int? otherSegment,string? otherPoint)=>
            DrawingConstraintOperands431.SameReference(first,segment,point,second,otherSegment,otherPoint,curves);
        return a.Kind==b.Kind&&Same(a.Third,a.ThirdSegmentIndex,null,b.Third,b.ThirdSegmentIndex,null)&&
            (Same(a.First,a.FirstSegmentIndex,a.FirstPoint,b.First,b.FirstSegmentIndex,b.FirstPoint)&&Same(a.Second,a.SecondSegmentIndex,a.SecondPoint,b.Second,b.SecondSegmentIndex,b.SecondPoint)||
             a.Second!=null&&Same(a.First,a.FirstSegmentIndex,a.FirstPoint,b.Second,b.SecondSegmentIndex,b.SecondPoint)&&Same(a.Second,a.SecondSegmentIndex,a.SecondPoint,b.First,b.FirstSegmentIndex,b.FirstPoint));
    }
    private static Relation FindDimension(DrawingProfileEdit edit,Network network)
    {
        var matches=network.Relations.Where(r=>r.Id.ToString().Equals(edit.ConstraintId,StringComparison.OrdinalIgnoreCase)).ToArray();
        Need(matches.Length==1&&matches[0].Spec?.Kind is "radius" or "diameter" or "length" or "distance" or "distance_x" or "distance_y" or "angle","OUT_OF_SCOPE","Select an exact supported dimension of the supplied component.");var relation=matches[0];
        if(relation.Spec!.Kind=="angle")Need(edit.Value>=DrawingAngularGeometry.MinimumDegrees&&edit.Value<=DrawingAngularGeometry.MaximumDegrees,"OUT_OF_SCOPE","Angular dimension values must remain0.1..179.9 degrees.");
        Need(relation.Expression==edit.ExpectedExpression&&relation.Value.HasValue&&DrawingProfileGeometry.Near(relation.Value.Value,edit.ExpectedValue!.Value),"EXPECTED_DIMENSION_MISMATCH","Dimension value or expression changed.");return relation;
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        var edit=(DrawingProfileEdit)args.Operations.Single();var binding=Resolve(edit,state,tx,cancel);
        if(edit.Action!="extrude_drawing_profile")RequireRetainedSweepEdit366(edit,binding,Consumers(binding.Selected,state,tx,cancel));
    }
}
