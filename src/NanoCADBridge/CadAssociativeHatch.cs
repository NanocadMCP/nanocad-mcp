using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Direct creation from actual native closed curves. Reuse the topology adapters
// accepted for retained association edits; no temporary rectangle or rebind.
internal static class CadAssociativeHatch
{
    internal sealed record Source(ObjectId Id,JsonElement Before);
    internal sealed record Boundary(AssociativeHatchSpec Spec,ObjectId[][] Groups,Vector3d Normal,double Elevation,
        CadHatchAssociationGeometry362.Boundary[] Geometry,Source[] Sources);
    static void Need(bool condition,string message,string code="UNSUPPORTED_HATCH_BOUNDARY")
    {if(!condition)throw new DomainException(code,message);}
    static string Handle(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    internal static Boundary Resolve(AssociativeHatchSpec spec,Database db,Transaction tr,ObjectId space,Action? cancel=null)
    {
        Action check=cancel??(()=>{});var groups=spec.Groups().Select(group=>group.Select(handle=>CadStructureWriter.Find(db,handle)).ToArray()).ToArray();
        var snapshots=new List<Source>();var capacity=new ModelInventoryBudget(check);
        foreach(var id in groups.SelectMany(group=>group))
        {
            check();var value=tr.GetObject(id,OpenMode.ForRead);var type=value.GetType();
            Need(value is Curve&&(type==typeof(Polyline)||type==typeof(Circle)||type==typeof(Ellipse)||type==typeof(Line)||type==typeof(Arc)||type==typeof(Spline))&&value.OwnerId==space&&value.Database==db,
                "Every boundary must be a local exact native curve in requested Model or active ordinary Paper space.");
            var source=(Entity)value;var layer=(LayerTableRecord)tr.GetObject(source.LayerId,OpenMode.ForRead);
            Need(!layer.IsLocked&&!layer.IsDependent,"Boundary layers must be local and unlocked.","LAYER_UNAVAILABLE");
            Need(!source.HasFields&&source.Annotative!=AnnotativeStates.True,"Field/annotative boundaries need their owning adapter.");
            // Retain classified local shared Hatch associations; unknown or
            // foreign persistent dependencies still need their owning route.
            using var links=source.GetPersistentReactorIds();
            foreach(ObjectId link in links)
            {
                check();var linked=tr.GetObject(link,OpenMode.ForRead);
                Need(linked is Hatch&&linked.GetType()==typeof(Hatch)&&linked.OwnerId==space&&linked.Database==db,
                    "Boundary has an unclassified or foreign persistent dependency.","UNSUPPORTED_HATCH_BOUNDARY_DEPENDENCY");
                var other=(Hatch)linked;using var ids=other.GetAssociatedObjectIds();
                Need(other.Associative&&ids.Cast<ObjectId>().Contains(id),"A retained Hatch reverse link has no matching forward source ID.","UNSUPPORTED_HATCH_BOUNDARY_DEPENDENCY");
            }
            var before=CadHatchEditingReader.CaptureBoundary358(source,tr,check);capacity.Add(before);snapshots.Add(new(id,before));
        }
        var outer=groups[0].Select(id=>(Entity)tr.GetObject(id,OpenMode.ForRead)).ToArray();
        Vector3d normal=outer.OfType<Polyline>().Select(p=>p.Normal).Concat(outer.OfType<Circle>().Select(p=>p.Normal))
            .Concat(outer.OfType<Ellipse>().Select(p=>p.Normal)).Concat(outer.OfType<Arc>().Select(p=>p.Normal)).FirstOrDefault();
        if(normal==default)normal=Vector3d.ZAxis;
        Need(double.IsFinite(normal.Length)&&Math.Abs(normal.Length-1)<=Tolerance.Global.EqualVector,"Native boundary plane must have a finite unit normal.","INVALID_GEOMETRY");
        Point3d point=((Curve)outer[0]).StartPoint;double elevation=point.TransformBy(Matrix3d.WorldToPlane(normal)).Z;
        using var probe=new Hatch{Normal=normal,Elevation=elevation,Associative=false};
        var geometry=CadHatchAssociationTopology362.Validate(groups.Select(group=>group.Select(id=>(Entity)tr.GetObject(id,OpenMode.ForRead)).ToArray()).ToArray(),probe,check);
        foreach(var source in snapshots)
        {
            check();var current=CadHatchEditingReader.CaptureBoundary358((Entity)tr.GetObject(source.Id,OpenMode.ForRead),tr,check);
            Need(BlockMoveGeometry.SameProperties(source.Before,current),"Native topology preparation changed a retained boundary source.","NATIVE_HATCH_SOURCE_CHANGED");
        }
        return new(spec,groups,normal,elevation,geometry,snapshots.ToArray());
    }
    static JsonElement WithoutOwnLink(JsonElement snapshot,string hatch)
    {
        var root=snapshot.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());
        var metadata=root["metadata"].EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());
        metadata["reactors"]=Wire.Element(metadata["reactors"].EnumerateArray().Where(id=>id.GetString()!=hatch).Select(id=>id.Clone()).ToArray());
        root["metadata"]=Wire.Element(metadata);return Wire.Element(root);
    }
    internal static void Complete(Hatch hatch,Boundary boundary,Transaction tr,Action? cancel=null)
    {
        Action check=cancel??(()=>{});var spec=boundary.Spec;
        foreach(var source in boundary.Sources){check();tr.GetObject(source.Id,OpenMode.ForWrite);}
        hatch.Normal=boundary.Normal;hatch.Elevation=boundary.Elevation;
        check();hatch.SetHatchPattern(HatchPatternType.PreDefined,spec.PatternName);
        bool solid=spec.PatternName.Equals("SOLID",StringComparison.OrdinalIgnoreCase);
        if(!solid){hatch.PatternScale=spec.PatternScale;hatch.PatternAngle=spec.PatternAngle;hatch.PatternDouble=false;}
        if(spec.OriginOcs is {} origin)hatch.Origin=new Point2d(origin.X,origin.Y);
        hatch.HatchStyle=spec.HatchStyle switch{"normal"=>HatchStyle.Normal,"outer"=>HatchStyle.Outer,_=>HatchStyle.Ignore};
        hatch.Associative=true;
        for(int index=0;index<boundary.Groups.Length;index++)
        {check();using var ids=new ObjectIdCollection(boundary.Groups[index]);hatch.AppendLoop(index==0?HatchLoopTypes.External:HatchLoopTypes.Default,ids);}
        foreach(var source in boundary.Sources)
        {
            check();var entity=(Entity)tr.GetObject(source.Id,OpenMode.ForWrite);using var current=entity.GetPersistentReactorIds();
            int count=current.Cast<ObjectId>().Count(id=>id==hatch.ObjectId);
            Need(count<=1,"Native Hatch supplied duplicate source reverse links.","NATIVE_HATCH_MISMATCH");
            if(count==0)entity.AddPersistentReactor(hatch.ObjectId);
        }
        check();hatch.EvaluateHatch(true);check();CadHatchAssociationGeometry362.Verify(hatch,boundary.Geometry,check);
        bool Near(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=1e-10+Math.Max(Math.Abs(a),Math.Abs(b))*1e-12;
        Need(hatch.Associative&&hatch.PatternType==HatchPatternType.PreDefined&&hatch.PatternName.Equals(spec.PatternName,StringComparison.OrdinalIgnoreCase)&&
            hatch.Normal==boundary.Normal&&Near(hatch.Elevation,boundary.Elevation)&&
            hatch.HatchStyle==(spec.HatchStyle switch{"normal"=>HatchStyle.Normal,"outer"=>HatchStyle.Outer,_=>HatchStyle.Ignore})&&
            (spec.OriginOcs==null||Near(hatch.Origin.X,spec.OriginOcs.X)&&Near(hatch.Origin.Y,spec.OriginOcs.Y))&&
            (solid||Near(hatch.PatternAngle,spec.PatternAngle)&&Near(hatch.PatternScale,spec.PatternScale)&&!hatch.PatternDouble),
            "Native Hatch did not retain requested source association and predefined pattern settings.","NATIVE_HATCH_MISMATCH");
        string own=Handle(hatch.ObjectId);
        foreach(var source in boundary.Sources)
        {
            check();var entity=(Entity)tr.GetObject(source.Id,OpenMode.ForRead);using var current=entity.GetPersistentReactorIds();
            Need(current.Cast<ObjectId>().Count(id=>id==hatch.ObjectId)==1,"Native Hatch did not retain exactly one reverse source link.","NATIVE_HATCH_MISMATCH");
            var actual=CadHatchEditingReader.CaptureBoundary358(entity,tr,check);
            Need(BlockMoveGeometry.SameProperties(WithoutOwnLink(source.Before,own),WithoutOwnLink(actual,own)),
                "Native Hatch creation changed its source geometry, properties, identity or other retained dependencies.","NATIVE_HATCH_MISMATCH");
        }
    }
}
