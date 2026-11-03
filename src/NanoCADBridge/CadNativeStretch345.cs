using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadNativeStretch345
{
    internal sealed record State(JsonElement Shape,NativeStretchPoint345[] Points,JsonElement Metadata,JsonElement Display,JsonElement Invariant);
    internal const string Warning="NATIVE_STRETCH_NON_DRIVING: Selected native points move in place; incident curve/arc geometry may change. This does not create or update driving associations. Inspect the retained curve and engineering result. Native Undo/recovery is not guaranteed.";
    private static void Need([DoesNotReturnIf(false)] bool valid,string message,string code="OUT_OF_SCOPE")
    {if(!valid)throw new DomainException(code,message);}
    internal static bool Supported(Type type)=>type==typeof(Line)||type==typeof(Polyline);
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static bool Same(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    private static double[] Point(Point3d value)
    {var p=new[]{value.X,value.Y,value.Z};Need(p.All(double.IsFinite),"Native stretch point is nonfinite.","NATIVE_STRETCH_READ_FAILED");return p;}
    private static double[] Vector(Vector3d value)=>Point(new Point3d(value.X,value.Y,value.Z));
    private static bool Near(Point3d a,Point3d b)=>a.DistanceTo(b)<=Tolerance.Global.EqualPoint;
    private static Point3d Point(double[] value)=>new(value[0],value[1],value[2]);

    private static NativeStretchPoint345[] Points(Curve source,Action cancel)
    {
        cancel();using var points=new Point3dCollection();source.GetStretchPoints(points);cancel();
        Need(points.Count>=0,"Native stretch point count is negative.","NATIVE_STRETCH_READ_FAILED");
        var result=new List<NativeStretchPoint345>();var budget=new CadContourBudget344(cancel);
        for(int i=0;i<points.Count;i++){cancel();var value=new NativeStretchPoint345(i,Point(points[i]));budget.Add(value);result.Add(value);}
        var values=result.ToArray();NativeStretchRead345.Validate(values,cancel);return values;
    }
    internal static NativeStretchObservation345 ReadProperties(Curve source,Action cancel)
    {
        cancel();Need(Supported(source.GetType()),"Native stretch inspection supports exact Line/Polyline.","UNSUPPORTED_ENTITY_TYPE");
        var shape=CadNativeCurveInspection342.Shape(source,cancel);var points=Points(source,cancel);
        var value=new NativeStretchObservation345(NativeStretchRead345.Token(shape,points,cancel),NativeCurveRead342.Token(shape,cancel),source.GetType().FullName!,points.Length,true,points,
            "Complete actual native GetStretchPoints indices/XYZ; point indices are not assumed vertex/grip ordinals. This read does not establish independent ownership or write admission.");
        NativeCurveRead342.CheckSerializedBudget(value,NativeCurveRead342.PublicGeometryByteBudget,cancel);return value;
    }
    internal static (NativeStretchObservation345? Value,string? Reason) TryReadProperties(Curve source,Action cancel)
    {
        try{return(ReadProperties(source,cancel),null);}
        catch(Teigha.Runtime.Exception error){cancel();return(null,"NATIVE_STRETCH_POINTS_UNAVAILABLE: "+error.ErrorStatus);}
        catch(DomainException error)when(error.Error.code=="NATIVE_STRETCH_READ_FAILED")
        {cancel();return(null,error.Error.code+": "+error.Message);}
    }
    private static JsonElement Display(Curve source)=>Wire.Element(new{layer_handle=Id(source.LayerId),linetype_handle=Id(source.LinetypeId),raw_color_index=source.ColorIndex,properties=EntityAdapter.StoredProperties(source)});
    private static JsonElement Metadata(Curve source,Transaction tr,Action cancel)
    {
        var budget=new CadContourBudget344(cancel);using var data=source.XData;using var links=source.GetPersistentReactorIds();var reactors=new List<string?>();
        foreach(ObjectId id in links){cancel();var reactor=Id(id);budget.Add(reactor!);reactors.Add(reactor);}
        var value=Wire.Element(new{root=new{handle=Id(source.ObjectId),owner_handle=Id(source.OwnerId),runtime_type=source.GetType().FullName,
            has_fields=source.HasFields,annotative=source.Annotative.ToString(),extension_handle=Id(source.ExtensionDictionary),
            xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add),reactors},owned=NativeOwnedMetadata.Capture(source,tr,cancel)});
        CadContourBudget344.Check(value,cancel);return value;
    }
    private static JsonElement Invariant(Curve source,Action cancel)
    {
        if(source is Line line)return Wire.Element(new{normal=Vector(line.Normal),thickness=line.Thickness,closed=line.Closed});
        var polyline=(Polyline)source;var widths=new List<object>();var budget=new CadContourBudget344(cancel);
        for(int i=0;i<polyline.NumberOfVertices;i++)
        {cancel();var value=new{start=polyline.GetStartWidthAt(i),end=polyline.GetEndWidthAt(i)};budget.Add(value);widths.Add(value);}
        var result=Wire.Element(new{normal=Vector(polyline.Normal),elevation=polyline.Elevation,thickness=polyline.Thickness,
            constant_width=polyline.ConstantWidth,closed=polyline.Closed,count=polyline.NumberOfVertices,widths});CadContourBudget344.Check(result,cancel);return result;
    }
    private static void Geometry(Curve source,Action cancel)
    {
        cancel();Need(Supported(source.GetType()),"Native stretch requires an exact Line/Polyline.","UNSUPPORTED_ENTITY_TYPE");
        bool XY(Point3d p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)&&Math.Abs(p.X)<=1e9&&Math.Abs(p.Y)<=1e9&&Math.Abs(p.Z)<=Tolerance.Global.EqualPoint;
        bool Normal(Vector3d n)=>n.IsEqualTo(Vector3d.ZAxis,Tolerance.Global);
        if(source is Line line)
            Need(XY(line.StartPoint)&&XY(line.EndPoint)&&Normal(line.Normal)&&line.Thickness==0&&!Near(line.StartPoint,line.EndPoint),"Native Line stretch requires a finite noncollapsed WCS XY/Z=0 zero-thickness source.","INVALID_GEOMETRY");
        else
        {
            var p=(Polyline)source;Need(p.NumberOfVertices>=2&&Normal(p.Normal)&&Math.Abs(p.Elevation)<=Tolerance.Global.EqualPoint&&p.Thickness==0,"Native Polyline stretch requires at least two vertices in WCS XY/Z=0 with zero thickness.","INVALID_GEOMETRY");
            Need(double.IsFinite(p.ConstantWidth),"Polyline width is nonfinite.","INVALID_GEOMETRY");
            for(int i=0;i<p.NumberOfVertices;i++)
            {
                cancel();Need(XY(p.GetPoint3dAt(i))&&double.IsFinite(p.GetBulgeAt(i))&&double.IsFinite(p.GetStartWidthAt(i))&&double.IsFinite(p.GetEndWidthAt(i)),"Polyline has nonfinite stored geometry.","INVALID_GEOMETRY");
                if(i+1<p.NumberOfVertices||p.Closed)Need(!Near(p.GetPoint3dAt(i),p.GetPoint3dAt((i+1)%p.NumberOfVertices)),"Native stretch may not collapse a Polyline segment.","DEGENERATE_GEOMETRY");
            }
        }
        _=CadNativeCurveInspection342.Shape(source,cancel);
    }
    internal static State Capture(Curve source,Transaction tr,Action cancel)
    {
        Geometry(source,cancel);var shape=CadNativeCurveInspection342.Shape(source,cancel);var points=Points(source,cancel);
        var result=new State(shape,points,Metadata(source,tr,cancel),Display(source),Invariant(source,cancel));CadContourBudget344.Check(result,cancel);return result;
    }
    internal static void Ownership(Curve source,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(source.Database==db&&!source.ObjectId.IsNull&&source.ObjectId.IsValid&&!source.ObjectId.IsErased&&!source.ObjectId.IsEffectivelyErased&&Supported(source.GetType()),"Requires an exact live resident native Line/Polyline.","UNSUPPORTED_ENTITY_TYPE");
        Need(HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Database==db,"Native stretch document is not active.","DOCUMENT_CONTEXT_MISMATCH");
        var space=CadBlockSpace.RequireSpace(db,tr,source.OwnerId);Need(CadBlockSpace.RequireWriteSpace(db,tr,space.Kind,cancel)==source.OwnerId,"Native stretch source is outside the requested active ordinary space.");
        using var refs=source.GetPersistentReactorIds();Need(!source.HasFields&&refs.Count==0&&source.Annotative!=AnnotativeStates.True,"Fields/reactors/annotative or associated curves require their owning edit route.","UNSUPPORTED_CURVE_DEPENDENCIES");
        _=NativeOwnedMetadata.Capture(source,tr,cancel);
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"))NativeOwner(source,db,tr,cancel);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void NativeOwner(Curve source,Database db,Transaction tr,Action cancel)
    {
        var document=Multicad.DatabaseServices.McDocument.ActiveDocument;
        Need(document!=null&&(string.IsNullOrEmpty(db.Filename)||string.Equals(Path.GetFullPath(db.Filename),Path.GetFullPath(document.Path),StringComparison.OrdinalIgnoreCase)),"Cannot bind the current native model document.","DOCUMENT_CONTEXT_MISMATCH");
        var id=Multicad.McObjectId.FromHandle(source.Handle.Value,document.ID);if(id.IsNull)return;
        Need(id.GetObjectOfType<Multicad.Mc3D.HistoryItem>()==null,"Source is part of native model history; use its owning model/sketch route.","UNSUPPORTED_CURVE_DEPENDENCIES");
        var model=((BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];var budget=new CadContourBudget344(cancel);
        foreach(ObjectId root in (BlockTableRecord)tr.GetObject(model,OpenMode.ForRead))
        {
            cancel();if(root.IsNull||!root.IsValid||root.IsErased||root.IsEffectivelyErased)continue;budget.Add(Id(root)!);
            if(tr.GetObject(root,OpenMode.ForRead) is not Solid3d)continue;
            var body=Multicad.McObjectId.FromHandle(root.Handle.Value,document.ID).GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();if(body==null)continue;
            var history=ModelInventoryBudget.History(body,cancel);
            foreach(var item in history)
            {
                cancel();budget.Add(item.ToString());Need(item!=id&&!(item.GetObjectOfType<Multicad.Mc3D.ProjectionSketchEnt>() is {} p&&p.SrcObjectID==id)&&
                    !(item.GetObjectOfType<Multicad.Mc3D.RevolveFeature>() is {} r&&r.Axis.ID==id),"Curve is consumed by native model history; use its owning edit route.","UNSUPPORTED_CURVE_DEPENDENCIES");
            }
            foreach(string reference in NativePathSources.ReferencedIds(history,cancel)){budget.Add(reference);Need(reference!=id.ToString(),"Curve supports native model sketch/path geometry; use its owning edit route.","UNSUPPORTED_CURVE_DEPENDENCIES");}
        }
    }
    private static State Prepare(Curve source,NativeStretchEdit345 edit,Database db,Transaction tr,Action cancel)
    {
        Ownership(source,db,tr,cancel);var before=Capture(source,tr,cancel);
        Need(NativeStretchRead345.Token(before.Shape,before.Points,cancel)==edit.ExpectedStretchToken,"Native stretch shape/point inventory changed; read its current complete token.","GEOMETRY_CHANGED");
        _=NativeStretchRead345.Expected(before.Points,edit,cancel);return before;
    }
    internal static void CheckSource(Entity source,NativeStretchEdit345 edit,Database db,Transaction tr,Action cancel)
    {Need(source is Curve&&Supported(source.GetType()),"Native stretch supports an exact Line/Polyline.","UNSUPPORTED_ENTITY_TYPE");_=Prepare((Curve)source,edit,db,tr,cancel);}
    internal static string[] Run(Curve source,State before,NativeStretchEdit345 edit,Action cancel)
    {
        // Installed IntegerCollection is a managed IList, not IDisposable;
        // MoveStretchPointsAt owns/releases its temporary native index array.
        var expected=NativeStretchRead345.Expected(before.Points,edit,cancel);var indices=new IntegerCollection();
        foreach(int index in edit.Indices){cancel();indices.Add(index);}
        cancel();source.MoveStretchPointsAt(indices,new(edit.DisplacementWcs.X,edit.DisplacementWcs.Y,edit.DisplacementWcs.Z));cancel();Geometry(source,cancel);
        var actual=Points(source,cancel);Need(actual.Length==expected.Length,"Native stretch changed the point inventory/topology.","NATIVE_STRETCH_MISMATCH");
        for(int i=0;i<actual.Length;i++){cancel();Need(Near(Point(actual[i].point_wcs),Point(expected[i].point_wcs)),"Native stretch failed a selected displacement or moved an unselected native point.","NATIVE_STRETCH_MISMATCH");}
        Need(BlockMoveGeometry.SameProperties(before.Invariant,Invariant(source,cancel))&&Same(before.Display,Display(source)),"Native stretch changed retained plane/closed state/explicit widths or appearance.","NATIVE_STRETCH_MISMATCH");
        var notices=new List<string>{Warning};
        if(source is Polyline polyline)
        {
            int count=0,first=-1;var vertices=before.Shape.GetProperty("geometry").GetProperty("vertices");
            for(int i=0;i<polyline.NumberOfVertices;i++){cancel();if(Math.Abs(vertices[i].GetProperty("bulge").GetDouble()-polyline.GetBulgeAt(i))>1e-12){if(first<0)first=i;count++;}}
            if(count>0)notices.Add("NATIVE_STRETCH_ARC_OBSERVATION: Native stretch changed "+count+" stored bulge(s), first vertex "+first+". Selected/unselected native point requirements passed; inspect the incident arc shape against the engineering intent.");
        }
        return notices.ToArray();
    }
    internal static void Preview(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,Action cancel)
    {
        var edit=(NativeStretchEdit345)args.Operations.Single();var source=(Curve)tr.GetObject(resolved.Sources[edit.Handles[0]],OpenMode.ForRead);
        var before=Prepare(source,edit,source.Database,tr,cancel);using var clone=CadContourBudget344.Detached(source);
        _=Run((Curve)clone,before,edit,cancel);var after=Capture(source,tr,cancel);
        Need(Same(before.Shape,after.Shape)&&Same(before.Metadata,after.Metadata)&&Same(before.Display,after.Display),"Native stretch preview changed its resident source.","NATIVE_STRETCH_MISMATCH");
    }
    internal static ModifiedEntities Apply(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,WriteJob job,List<string>? warnings=null)
    {
        var edit=(NativeStretchEdit345)args.Operations.Single();var source=(Curve)tr.GetObject(resolved.Sources[edit.Handles[0]],OpenMode.ForRead);
        Need(source.OwnerId==resolved.Model&&CadBlockSpace.RequireWriteSpace(source.Database,tr,args.TargetSpace,job.CheckCancellation)==resolved.Model,"Native stretch source differs from requested Model/Paper space.");
        var before=Prepare(source,edit,source.Database,tr,job.CheckCancellation);var reference=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles[0]);
        var result=new ModifiedEntities(args.OperationId,"committed",false,[reference],[],[],[],"not_guaranteed");CadContourBudget344.Output(result,job.CheckCancellation);
        source.UpgradeOpen();var notices=Run(source,before,edit,job.CheckCancellation);var after=Capture(source,tr,job.CheckCancellation);
        Need(Same(before.Metadata,after.Metadata)&&Same(before.Display,after.Display),"Native stretch changed source identity/owner/application or classified owned data.","NATIVE_STRETCH_MISMATCH");warnings?.AddRange(notices);return result;
    }
}
