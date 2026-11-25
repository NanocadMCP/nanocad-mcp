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
    private sealed record Binding(McDocument Sheet,Dictionary<string,Source> Selected,Network Network,
        SketchMobilityReport? Plan = null, SketchConstraintSpec[]? Effective = null);
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
        var frame=selected.Values.First().Workplane;using var manager=Manager(sheet,frame);
        if(manager==null)return new(false,new(selected,StringComparer.OrdinalIgnoreCase),Array.Empty<Relation>());
        var inventory=NativeConstraintInventory.Read(manager,()=>selected.Values.Select(s=>s.Native),cancel);
        var ids=inventory.Ids;
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
                spec=DecodeConstraint(info,value,Source,frame);
            }
            string references=string.Join("|",refs.Select(p=>p!.ObjectId+":"+p.SubentId+":"+p.PolylineSegmentIndex));
            inventoryBudget.Add(new{id=id.ToString(),type=info.Type.ToString(),variable=info.VarId.ToString(),name,expression,value,references,spec});
            relations.Add(new(id,info.Type.ToString(),spec,info.VarId,name,expression,value,references));
        }
        return new(inventory.Complete,sources,relations.ToArray());
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
        if(edit.Action is "add_drawing_constraints" or "set_drawing_dimension")
        {
            var replaced=edit.Action=="set_drawing_dimension"?FindDimension(edit,network):null;
            var proposed=replaced==null?edit.Constraints!:new[]{replaced.Spec! with{Value=edit.Value}};
            var plan=Assess(binding.Selected.Values.Select(source=>source.Curve),network,proposed,replaced==null?null:new(){replaced.Id},cancel,edit.PreserveMotion);
            var effective=replaced==null?plan.EffectiveCandidates(proposed, edit.RequireVerifiedConstraints):proposed;
            if(replaced!=null)plan.RequireCompatibleReplacement();
            if(edit.Action=="add_drawing_constraints")
                DrawingProfileGeometry.Validate(proposed,selected.Values.Select(s=>s.Curve).ToArray());
            binding=binding with{Plan=plan,Effective=effective};
        }
        if(IsRemoval(edit))PrepareRemoval(edit,binding,cancel);
        if(DrawingVariablePlanning.IsAction(edit.Action))
        {
            var variables=PrepareVariables(edit,binding,state,cancel);
            if(edit.Action=="set_drawing_expression")
            {
                var changed=network.Relations.Where(r=>r.Spec!=null&&!r.Variable.IsNull&&variables.Affected.Contains(Key(r.Variable))&&
                    r.Value.HasValue&&variables.Values[Key(r.Variable)]!=r.Value.Value).ToArray();
                if(changed.Length!=0)
                {
                    var proposed=changed.Select(r=>r.Spec! with{Value=variables.Values[Key(r.Variable)]}).ToArray();
                    var plan=Assess(selected.Values.Select(s=>s.Curve),network,proposed,changed.Select(r=>r.Id).ToHashSet(),cancel);
                    plan.RequireCompatibleReplacement();binding=binding with{Plan=plan};
                }
            }
        }
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
        if(edit.Action!="extrude_drawing_profile"&&binding.Effective is not {Length:0})RequireRetainedSweepEdit366(edit,binding,Consumers(binding.Selected,state,tx,cancel));
    }
}
