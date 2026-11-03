using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using MPoint=Multicad.Geometry.Point3d;
using MVector=Multicad.Geometry.Vector3d;
using MPlane=Multicad.Geometry.Plane3d;

namespace NanoCADBridge;

internal static class ModelExtrusionFactory
{
    internal sealed record Items(PlanarSketch Sketch,SketchProfile Profile,ExtrudeFeature Feature);
    private static MPoint Point(Point3 p)=>new(p.X,p.Y,p.Z);
    private static MVector Vector(Point3 p)=>new(p.X,p.Y,p.Z);
    private static void Need(bool valid,string message){if(!valid)throw new DomainException("MODEL_UPDATE_FAILED",message);}
    private sealed record ModelDestination418(McDocument Drawing,McDocument Top,McObjectId ModelSpaceId,McDocument Model);
    private static ModelDestination418 ResolveModelDestination()
    {
        var drawing=McDocument.ActiveDocument;
        var top=drawing?.TopDoc;
        McObjectId modelId=McObjectId.Null;
        McDocument? model=null;
        object Details()=>new{component="fresh_model_document_resolution",drawing_document_id=drawing?.ID.ToString(),
            top_document_id=top?.ID.ToString(),model_space_id=modelId.IsNull?null:modelId.ToString(),resolved_document_id=model?.ID.ToString()};
        if(drawing==null||drawing.ID.IsNull||top==null||top.ID.IsNull||top.IsSub)
            throw new DomainException("MODEL_UPDATE_FAILED","New Model construction needs the actual top drawing document.",false,Details());
        // The SDK defines ModelSpaceId only on the top document. Insertion in
        // that container can target its active sheet; resolve the exact Model
        // subdocument instead of treating container and resident owner as equal.
        modelId=top.ModelSpaceId;
        if(!modelId.IsNull)model=modelId.GetObjectOfType<McDocument>();
        if(model==null||model.ID!=modelId||model.TopDoc?.ID!=top.ID)
            throw new DomainException("MODEL_UPDATE_FAILED","The top drawing's exact native Model document is unavailable.",false,Details());
        return new(drawing,top,modelId,model);
    }
    internal static McDocument ModelDocument()=>ResolveModelDestination().Model;
    private static void RegisterFresh(McDbObject value,McDocument owner,string role,ModelDestination418? destination=null)
    {
        var actual=value.Document;
        string? before=actual?.ID.ToString();bool attempted=false;bool? added=null;
        object Details()
        {
            object? mapping=null;
            if(destination!=null)mapping=new{drawing_document_id=destination.Drawing.ID.ToString(),top_document_id=destination.Top.ID.ToString(),
                model_space_id=destination.ModelSpaceId.ToString(),resolved_document_id=destination.Model.ID.ToString()};
            else try
            {
                var top=owner.TopDoc;
                mapping=new{top_document_id=top?.ID.ToString(),model_space_id=top==null||top.IsSub?null:top.ModelSpaceId.ToString()};
            }
            catch(Exception error){mapping=new{unavailable_error_type=error.GetType().Name};}
            return new{component="new_native_construction_registration",role,value_id=value.ID.ToString(),value_runtime_type=value.GetType().FullName,
                requested_document_id=owner.ID.ToString(),actual_document_id_before=before,actual_document_id=actual?.ID.ToString(),
                registration_attempted=attempted,registration_returned=added,authoritative_model_space=mapping};
        }
        void Require(bool valid,string message){if(!valid)throw new DomainException("MODEL_UPDATE_FAILED",message,false,Details());}
        Require(actual==null||actual.ID==owner.ID,"New native construction belongs to another document; no owner transfer is attempted.");
        if(actual==null)
        {
            attempted=true;added=McObjectManager.Add2Document(value,owner);actual=value.Document;
            Require(added==true,"Native construction registration in its explicit owner document failed.");
        }
        actual=value.Document;
        Require(actual!=null&&actual.ID==owner.ID,"New native construction lacks residency in its actual owner document.");
    }
    internal static Items Append(Mc3dSolid body,ModelExtrusionSpec spec,string? operation,Action cancel,Action<string> trace,
        McObjectId[]? externalSources=null,Action<PlanarSketch>? nativeInitializer=null,Action<PlanarSketch,SketchProfile?>? observeConstruction=null)
    {
        // The old appended AddGeometry(NewID) chain lost its sketch/profile/
        // feature on reopen in412. Only an owned resident initializer or the
        // separately supported retained-source routes may append this contour.
        if(operation!=null&&externalSources is not {Length:>0}&&!spec.EditableProfile&&spec.NativeGeometry==null&&nativeInitializer==null)
            throw new DomainException("INLINE_PROFILE_APPEND_PERSISTENCE_UNAVAILABLE","Appending raw inline sketch geometry without retained native inputs is unavailable: its history did not survive save/reopen.");
        var normal=spec.NativeGeometry==null?ModelExtrusionGeometry.Measure(spec,cancel).Normal:DrawingWorkplane.Normal(spec.Plane);
        var (sketch,profile)=CreateProfile(body,spec,cancel,trace,externalSources,nativeInitializer:nativeInitializer,observeConstruction:observeConstruction);
        return AppendProfile(body,spec,sketch,profile,operation,trace,Vector(normal));
    }
    internal static Items AppendProfile(Mc3dSolid body,ModelExtrusionSpec spec,PlanarSketch sketch,SketchProfile profile,string? operation,Action<string> trace,MVector? requestedNormal=null)
    {
        var normal=requestedNormal??Vector(DrawingWorkplane.Normal(spec.Plane));
        bool positive=spec.Direction=="positive";
        using(var plane=sketch.Plane)if(plane.SignedDistanceTo(Point(spec.Plane.Origin)+normal)<0)positive=!positive;
        var extrusion=body.AddExtrudeFeature(profile.ID,spec.Distance,0,positive?FeatureExtentDirection.Positive:FeatureExtentDirection.Negative);
        // MultiCAD creates feature variables during UpdateAll, as in the accepted
        // CoreParameterProbe. Reading DistanceParamVarID before that rebuild can
        // return Null for a valid newly added feature.
        Need(extrusion!=null,"Native extrusion was not created.");
        extrusion!.Name=spec.Name+" Extrusion";
        if(operation!=null)
        {
            extrusion.PartOperation=operation=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join;
        }
        extrusion.DbEntity.Update();
        trace("extrusion_created");return new(sketch,profile,extrusion);
    }
    internal static (PlanarSketch Sketch,SketchProfile Profile) CreateProfile(Mc3dSolid? body,ModelExtrusionSpec spec,Action cancel,Action<string> trace,
        McObjectId[]? externalSources=null,McDocument? constructionDocument=null,Action<PlanarSketch>? nativeInitializer=null,
        Action<PlanarSketch,SketchProfile?>? observeConstruction=null)
    {
        var normal=spec.NativeGeometry==null?ModelExtrusionGeometry.Measure(spec,cancel).Normal:DrawingWorkplane.Normal(spec.Plane);cancel();
        if(spec.NativeGeometry!=null)
            Need(nativeInitializer!=null&&spec.RegionPoint!=null&&externalSources==null,"Controlled first-feature creation needs its native initializer and selected region.");
        Need(body!=null||spec.NativeGeometry!=null&&constructionDocument!=null,"Standalone controlled sketch creation needs its explicit actual document.");
        var destination=body==null||spec.EditableProfile?ResolveModelDestination():null;
        var owner=body==null?constructionDocument:body.DbEntity.Document;
        Need(owner!=null&&!owner.ID.IsNull,"Native sketch creation needs its actual owner document.");
        if(body==null)Need(owner!.ID==destination!.ModelSpaceId,"Standalone construction must use the exact native Model document.");
        if(constructionDocument!=null)
            Need(owner!.ID==constructionDocument.ID,"The explicit construction document is not the selected body's actual native owner.");
        var sketch=body==null?new PlanarSketch():body.AddPlanarSketch();Need(sketch!=null,"Native sketch was not created.");
        observeConstruction?.Invoke(sketch!,null);
        sketch!.Name=spec.Name+" Sketch";sketch.DbEntity.Layer=spec.Layer;sketch.DbEntity.Visibility=0;
        // AddPlanarSketch already registers in body.DbEntity.Document in the
        // installed managed SDK. Do not re-register it in the active drawing.
        RegisterFresh(sketch.DbEntity,owner!,"sketch",destination);
        bool initializeNative=spec.NativeGeometry!=null||nativeInitializer!=null;
        Need(nativeInitializer==null||externalSources==null&&!spec.EditableProfile,"Native owned initialization cannot be mixed with retained drawing-source construction.");
        using(var plane=new MPlane(Point(spec.Plane.Origin),Vector(spec.Plane.XAxis),Vector(spec.Plane.YAxis)))
        {if(!initializeNative)sketch.SetPlane(plane);else Need(sketch.SetPlane(plane,false),"Native sketch rejected its explicit coordinate frame.");}
        if(initializeNative)sketch.DbEntity.Update(); // SDK SetPlane -> Update -> EnterEditMode.
        using(var plane=sketch.Plane)
        {
            var origin=Point(spec.Plane.Origin);var x=origin+Vector(spec.Plane.XAxis);var y=origin+Vector(spec.Plane.YAxis);
            Need(Math.Abs(plane.SignedDistanceTo(origin))<1e-7&&Math.Abs(plane.SignedDistanceTo(x))<1e-7&&Math.Abs(plane.SignedDistanceTo(y))<1e-7,"Native sketch plane differs from the requested WCS plane.");
        }
        trace("sketch_plane_verified");
        void Add(Multicad.Geometry.EntityGeometry geometry)
        {
            if(!spec.EditableProfile){Need(sketch.AddGeometry(McObjectId.NewID(),geometry),"Native sketch rejected geometry.");return;}
            // AddObject was observed to internalize ordinary curves in175.
            // Explicit projection retains the real source, as in the accepted
            // drawing-profile route, and exposes an unambiguous source ID.
            var source=new Multicad.DatabaseServices.StandardObjects.DbGeometry();source.Geometry=geometry;
            // This branch intentionally retains an external drawing source.
            // Preserve that source scope separately from the sketch's owner.
            var sourceOwner=destination!.Model;
            source.DbEntity.Layer=spec.Layer;RegisterFresh(source.DbEntity,sourceOwner,"retained_source",destination);source.DbEntity.Update();
            var projection=sketch.ProjectObject(source.ID,true);Need(projection!=null&&projection.SrcObjectID==source.ID&&projection.HostSketchID==sketch.ID,"Native sketch rejected the retained profile projection.");
            projection!.DbEntity.Layer=spec.Layer;projection.DbEntity.Visibility=0;
            RegisterFresh(projection.DbEntity,owner!,"projection",destination);
            source.DbEntity.Visibility=0;source.DbEntity.Update();
        }
        if(initializeNative)
        {
            nativeInitializer!(sketch);
            Need(!sketch.IsInEditMode,"Native controlled initialization did not finish sketch editing.");
            trace("native_sketch_initialized");
        }
        else if(externalSources is {Length:>0})
        {
            foreach(var source in externalSources)
            {
                cancel();var projection=sketch.ProjectObject(source);
                Need(projection!=null&&projection.SrcObjectID==source&&projection.HostSketchID==sketch.ID,
                    "Native sketch rejected an exact drawing-source projection.");
                RegisterFresh(projection!.DbEntity,owner!,"projection",destination);
            }
            McObjectManager.UpdateAll();
        }
        else if(spec.Profile.Kind=="circle")
        {
            using var circle=new Multicad.Geometry.CircArc3d(Point(ModelExtrusionGeometry.World(spec.Plane,spec.Profile.Center!)),Vector(normal),spec.Profile.Radius);
            Add(circle);
        }
        else if(spec.Profile.Kind=="polycurve")
        {
            foreach(var curve in ModelPolycurve.Curves(spec.Profile,cancel))
            {
                cancel();var start=Point(ModelExtrusionGeometry.World(spec.Plane,curve.Start!));var end=Point(ModelExtrusionGeometry.World(spec.Plane,curve.End!));
                if(curve.Kind=="line")
                {using var line=new Multicad.Geometry.LineSeg3d(start,end);Add(line);}
                else
                {var mid=Point(ModelExtrusionGeometry.World(spec.Plane,ModelPolycurve.Midpoint(curve)));using var arc=new Multicad.Geometry.CircArc3d(start,mid,end);Add(arc);}
            }
        }
        else
        {
            var vertices=spec.Profile.Vertices!;
            for(int i=0;i<vertices.Length;i++)
            {
                cancel();using var line=new Multicad.Geometry.LineSeg3d(Point(ModelExtrusionGeometry.World(spec.Plane,vertices[i])),Point(ModelExtrusionGeometry.World(spec.Plane,vertices[(i+1)%vertices.Length])));
                Add(line);
            }
        }
        // SDK Sample3dObject updates the completed sketch before creating its
        // profile, including the direct AddGeometry (non-editor) path.
        if(spec.NativeGeometry==null)sketch.DbEntity.Update();
        trace("sketch_geometry_added");
        var profile=sketch.CreateProfile();Need(profile!=null,"Native sketch profile was not created.");
        observeConstruction?.Invoke(sketch,profile);
        profile!.Name=spec.Name+" Profile";profile.DbEntity.Layer=spec.Layer;profile.DbEntity.Visibility=0;
        RegisterFresh(profile.DbEntity,owner!,"profile",destination);
        if(spec.NativeGeometry==null)
        {
            Need(profile.AutoProcessExternalContours(),"Native sketch did not produce a closed external contour.");
            profile.DbEntity.Update();
        }
        else
        {
            Need(profile.Clear()&&profile.IsEmpty,"The new controlled profile did not clear its selected regions.");
            using var ray=new Multicad.Geometry.Line3d(Point(spec.RegionPoint!),Vector(normal));
            Need(profile.AddOrRemoveRegionByLine(ray)&&!profile.IsEmpty,"The solved native sketch did not supply the requested region.");
            profile.DbEntity.Update();
            ModelProfileLifecycleState340.RequireRegionPoint(profile,spec.Plane,spec.RegionPoint!,cancel);
        }
        trace("profile_processed");
        return (sketch,profile);
    }

}
