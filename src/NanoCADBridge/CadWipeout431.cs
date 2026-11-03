using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// A Wipeout is its native RasterImage-derived mask, without an external source
// file. HasFrame is an installed managed stub and is deliberately never called.
internal static class CadWipeout431
{
    internal sealed record Shape(Point3d[] Boundary,Vector3d Normal,SketchPlaneSpec Frame);
    sealed record State(Shape Shape,JsonElement Display,JsonElement Metadata,JsonElement? Owned);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool yes,string message,string code="NATIVE_WIPEOUT_MISMATCH")
    {if(!yes)throw new DomainException(code,message);}
    static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    static bool Same(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    static double[] P(Point3d p)=>[p.X,p.Y,p.Z];
    static bool Near(Point3d a,Point3d b)=>a.DistanceTo(b)<=Tolerance.Global.EqualPoint;
    internal static bool Ordinary(EditSpec operation)=>operation is MoveSpec or RotateSpec or ScaleSpec or CopySpec or EraseSpec or PropertiesSpec or WipeoutBoundaryEdit431;
    internal static Shape Geometry(Wipeout mask,Action cancel)
    {
        cancel();Need(mask.GetType()==typeof(Wipeout),"Select an exact native Wipeout.","UNSUPPORTED_ENTITY_TYPE");
        using var points=mask.GetClipBoundary();var matrix=mask.PixelToModelTransform;
        var world=points.Cast<Point2d>().Select(p=>new Point3d(p.X,p.Y,0).TransformBy(matrix)).ToArray();
        if(world.Length>1&&Near(world[0],world[^1]))world=world[..^1];
        Need(world.Length>=3&&world.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)),"Native mask boundary is unavailable or nonfinite.","INVALID_GEOMETRY");
        var orientation=mask.Orientation;var normal=orientation.Xaxis.CrossProduct(orientation.Yaxis);Need(normal.Length>0&&double.IsFinite(normal.Length),"Native mask plane is degenerate.","INVALID_GEOMETRY");normal=normal.GetNormal();
        var x=world[1]-world[0];Need(x.Length>0,"Native mask has a collapsed boundary edge.","INVALID_GEOMETRY");x=x.GetNormal();var y=normal.CrossProduct(x).GetNormal();
        var frame=new SketchPlaneSpec(new(world[0].X,world[0].Y,world[0].Z),new(x.X,x.Y,x.Z),new(y.X,y.Y,y.Z));
        var local=world.Select(p=>DrawingWorkplane.Local(frame,new(p.X,p.Y,p.Z))).ToArray();SimplePolygonValidation.Validate(local,cancel:cancel);
        SerializedCapacity357.Check(world.Select(P).ToArray(),cancel:cancel);return new(world,normal,frame);
    }
    static State Capture(Wipeout mask,Transaction tr,Action cancel)
    {
        var geometry=Geometry(mask,cancel);using var data=mask.XData;using var reactors=mask.GetPersistentReactorIds();var links=new List<string?>();
        foreach(ObjectId id in reactors){cancel();if(!mask.ObjectId.IsNull&&CadDrawingGroups431.OrganizationalReactor(mask,id,tr,cancel))continue;links.Add(Id(id));}
        var budget=new ModelInventoryBudget(cancel);var metadata=Wire.Element(new{handle=Id(mask.ObjectId),owner=Id(mask.OwnerId),image_definition=Id(mask.ImageDefId),mask.HasFields,
            extension=Id(mask.ExtensionDictionary),xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add),reactors=links.OrderBy(v=>v,StringComparer.Ordinal).ToArray()});
        JsonElement? owned=mask.ObjectId.IsNull?null:NativeOwnedMetadata.Capture(mask,tr,cancel);
        var result=new State(geometry,Wire.Element(new{layer=Id(mask.LayerId),properties=EntityAdapter.StoredProperties(mask)}),metadata,owned);
        SerializedCapacity357.Check(new{geometry=GeometryValue(geometry),result.Display,result.Metadata,result.Owned},cancel:cancel);return result;
    }
    static object GeometryValue(Shape shape)=>new{boundary_world=shape.Boundary.Select(P).ToArray(),normal=new[]{shape.Normal.X,shape.Normal.Y,shape.Normal.Z}};
    static string Token(State state,Action cancel)
    {var payload=new{geometry=GeometryValue(state.Shape),state.Display,state.Metadata,state.Owned};SerializedCapacity357.Check(payload,cancel:cancel);return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload,Wire.Json)));}
    internal static object Read(Wipeout mask,Action cancel)
    {
        try
        {
            var tr=mask.Database.TransactionManager.TopTransaction??throw new DomainException("OBJECT_READ_FAILED","Mask token needs its current read transaction.");var state=Capture(mask,tr,cancel);
            return new{geometry_token=Token(state,cancel),boundary_world=state.Shape.Boundary.Select(P).ToArray(),normal=new[]{state.Shape.Normal.X,state.Shape.Normal.Y,state.Shape.Normal.Z},
                native_mask=true,frame_display_scope="controlled_by_nanoCAD_global_settings_unchanged",editing_unavailable_reason=(string?)null};
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error)
        {
            string reason=error is DomainException d?d.Error.code+": "+d.Message:error.GetType().Name+": "+error.Message;if(reason.Length>1024)reason=reason[..1024];
            return new{geometry_token=(string?)null,boundary_world=(double[][]?)null,normal=(double[]?)null,native_mask=true,frame_display_scope="controlled_by_nanoCAD_global_settings_unchanged",editing_unavailable_reason=reason};
        }
    }
    static bool SameBoundary(Point3d[] expected,Point3d[] actual)
    {
        // Native masks may omit redundant collinear points or choose another
        // starting vertex/winding. Compare the same polygon, not vertex storage.
        Point3d[] Normalize(Point3d[] input)
        {
            var points=input.ToList();if(points.Count>1&&Near(points[0],points[^1]))points.RemoveAt(points.Count-1);
            bool changed=true;while(changed&&points.Count>3)
            {
                changed=false;for(int i=0;i<points.Count;i++)
                {
                    var a=points[(i+points.Count-1)%points.Count];var p=points[i];var b=points[(i+1)%points.Count];var direction=b-a;double square=direction.DotProduct(direction),along=(p-a).DotProduct(direction);
                    if(square>0&&along>=0&&along<=square&&Near(p,a+direction*(along/square))){points.RemoveAt(i);changed=true;break;}
                }
            }
            return points.ToArray();
        }
        expected=Normalize(expected);actual=Normalize(actual);
        if(expected.Length!=actual.Length)return false;
        for(int offset=0;offset<actual.Length;offset++)foreach(int direction in new[]{1,-1})
        {bool same=true;for(int i=0;i<expected.Length;i++)if(!Near(expected[i],actual[(offset+direction*i+actual.Length)%actual.Length])){same=false;break;}if(same)return true;}return false;
    }
    static void SetLocal(Wipeout mask,Point2[] polygon,Action cancel)
    {
        SimplePolygonValidation.Validate(polygon,cancel:cancel);using var points=new Point2dCollection(polygon.Select(p=>new Point2d(p.X,p.Y)).Append(new Point2d(polygon[0].X,polygon[0].Y)).ToArray());
        cancel();mask.SetFrom(points,Vector3d.ZAxis);cancel();
    }
    internal static Wipeout CreateLocal(WipeoutSpec431 spec,Action? cancellation=null)
    {
        var cancel=cancellation??(()=>{});var mask=new Wipeout();
        try{SetLocal(mask,spec.Boundary,cancel);var after=Geometry(mask,cancel);Need(SameBoundary(spec.Boundary.Select(p=>new Point3d(p.X,p.Y,0)).ToArray(),after.Boundary),"Created native mask does not match the requested boundary.");return mask;}
        catch{mask.Dispose();throw;}
    }
    internal static void CheckSource(Entity source,IReadOnlyCollection<EditSpec> edits,Database db,Transaction tr,Action cancel)
    {
        Need(source.GetType()==typeof(Wipeout)&&edits.Count>0&&edits.All(Ordinary),"Native Wipeout supports boundary, placement, copy, properties and erase through its owning route.","UNSUPPORTED_ENTITY_TYPE");
        Need(source.Database?.UnmanagedObject==db.UnmanagedObject&&!source.ObjectId.IsNull&&!source.ObjectId.IsErased&&!source.ObjectId.IsEffectivelyErased,"Mask source is not live in the requested drawing.","OBJECT_NOT_FOUND");
        Need(tr.GetObject(source.OwnerId,OpenMode.ForRead) is BlockTableRecord {IsLayout:true},"Nested masks require their owning definition route.","OUT_OF_SCOPE");
        CadIndependentCurve.Require(source,db,tr,cancel);Need(source.Annotative!=AnnotativeStates.True,"Mask annotation contexts require their owning adapter.","UNSUPPORTED_WIPEOUT_DEPENDENCIES");
        var state=Capture((Wipeout)source,tr,cancel);
        foreach(var edit in edits.OfType<WipeoutBoundaryEdit431>())
        {Need(Token(state,cancel)==edit.ExpectedGeometryToken,"Mask geometry/owner/display changed; refresh its current token.","GEOMETRY_CHANGED");_=Resolve(state.Shape,edit,cancel);}
    }
    static Point2[] Resolve(Shape source,WipeoutBoundaryEdit431 edit,Action cancel)
    {
        var points=edit.BoundaryWorld.Select(p=>{cancel();return DrawingWorkplane.Local(source.Frame,p);}).ToArray();SimplePolygonValidation.Validate(points,cancel:cancel);return points;
    }
    internal static Wipeout Detached(Wipeout source)
    {
        var candidate=source.Clone();var copy=candidate as Wipeout;
        if(copy==null||copy.GetType()!=typeof(Wipeout)||!copy.AutoDelete||!copy.ObjectId.IsNull||!copy.OwnerId.IsNull||copy.UnmanagedObject==IntPtr.Zero||copy.UnmanagedObject==source.UnmanagedObject)
        {if(candidate!=null&&!ReferenceEquals(candidate,source))GC.SuppressFinalize(candidate);throw new DomainException("UNSAFE_NATIVE_CLONE","Mask clone is not a distinct detached owned native Wipeout.");}return copy;
    }
    internal static void SetBoundary(Wipeout mask,WipeoutBoundaryEdit431 edit,Transaction tr,Action cancel,bool resident=true)
    {
        var before=Capture(mask,tr,cancel);if(resident)Need(Token(before,cancel)==edit.ExpectedGeometryToken,"Mask changed before mutation; refresh current geometry_token.","GEOMETRY_CHANGED");
        // SetFrom builds local geometry; its placement transform is an internal
        // implementation step, not a requested move of application XData points.
        using var originalData=mask.XData;var polygon=Resolve(before.Shape,edit,cancel);SetLocal(mask,polygon,cancel);mask.TransformBy(DrawingWorkplaneNative.Matrix(before.Shape.Frame));
        if(originalData!=null)
        {
            using var currentData=mask.XData;var budget=new ModelInventoryBudget(cancel);var current=JsonSerializer.SerializeToElement<object?>(currentData==null?null:NativeOwnedMetadata.Buffer(currentData,cancel,budget.Add),Wire.Json);
            if(!Same(before.Metadata.GetProperty("xdata"),current))mask.XData=originalData;
        }
        if(!mask.ObjectId.IsNull)mask.RecordGraphicsModified(true);
        var after=Capture(mask,tr,cancel);Need(SameBoundary(edit.BoundaryWorld.Select(p=>new Point3d(p.X,p.Y,p.Z)).ToArray(),after.Shape.Boundary),"Native mask boundary does not match requested WCS contour.");
        Need(Same(before.Display,after.Display)&&Same(before.Metadata,after.Metadata)&&before.Owned.HasValue==after.Owned.HasValue&&(!before.Owned.HasValue||Same(before.Owned.Value,after.Owned!.Value)),"Native mask boundary edit changed retained identity, display or owned data.");
    }
    static JsonElement WithoutXData(JsonElement metadata)=>Wire.Element(metadata.EnumerateObject().Where(p=>p.Name!="xdata").ToDictionary(p=>p.Name,p=>p.Value.Clone(),StringComparer.Ordinal));
    internal static void Transform(Wipeout mask,EditSpec edit,Transaction tr,Action cancel)
    {
        var before=Capture(mask,tr,cancel);var matrix=CadModifier.Matrix(edit);var expected=before.Shape.Boundary.Select(p=>p.TransformBy(matrix)).ToArray();JsonElement data;
        using(var clone=Detached(mask)){clone.XDataTransformBy(matrix);using var xdata=clone.XData;var budget=new ModelInventoryBudget(cancel);data=JsonSerializer.SerializeToElement<object?>(xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,budget.Add),Wire.Json);}
        cancel();mask.TransformBy(matrix);using(var xdata=mask.XData){var budget=new ModelInventoryBudget(cancel);var actual=JsonSerializer.SerializeToElement<object?>(xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,budget.Add),Wire.Json);if(!Same(data,actual)&&Same(before.Metadata.GetProperty("xdata"),actual))mask.XDataTransformBy(matrix);}
        if(!mask.ObjectId.IsNull)mask.RecordGraphicsModified(true);var after=Capture(mask,tr,cancel);
        Need(SameBoundary(expected,after.Shape.Boundary)&&Same(before.Display,after.Display)&&Same(WithoutXData(before.Metadata),WithoutXData(after.Metadata))&&Same(data,after.Metadata.GetProperty("xdata"))&&
            before.Owned.HasValue==after.Owned.HasValue&&(!before.Owned.HasValue||Same(before.Owned.Value,after.Owned!.Value)),"Native mask transform changed requested geometry or retained display/application data.");
    }
}
