using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;
using static NanoCADBridge.CadDrawingViewEditState;

namespace NanoCADBridge;

internal static class CadSectionCutState351
{
    internal sealed record SectionState(Section Native,Db.ObjectId Entity,JsonElement Metadata,JsonElement Owned,string Token);
    internal sealed record Plan(SetSectionViewCut351 Edit,Snapshot Drawing,SectionState Section,View[] Views,Dictionary<McObjectId,JsonElement> Owned);
    internal static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool value,string code,string message)
    {if(!value)throw new DomainException(code,message);}
    static string H(Db.ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    static string? Optional(Db.ObjectId id)=>id.IsNull?null:H(id);
    static object Color(Teigha.Colors.Color color)=>new{method=color.ColorMethod.ToString(),index=color.ColorIndex,color.Red,color.Green,color.Blue,color.HasColorName,color.ColorName,color.BookName};
    internal static object Settings(Db.Section native,Db.Transaction tx,Action cancel)
    {
        if(native.Settings.IsNull)return new{handle=(string?)null};
        var id=native.Settings;Require(!Gone(id),"SECTION_SETTINGS_UNAVAILABLE","Retained native Section settings are unavailable.");
        using var settingsBinding=SectionNativeBinding357.Settings(tx.GetObject(id,Db.OpenMode.ForRead),native.Database,cancel);var settings=settingsBinding.Value;
        Require(settings.ObjectId==id,"SECTION_SETTINGS_UNAVAILABLE","Retain exact native Section settings in this drawing.");
        using var xdata=settings.XData;using var links=settings.GetPersistentReactorIds();var bytes=new ModelInventoryBudget(cancel);
        var reactors=new List<string>();foreach(Db.ObjectId reference in links){cancel();Require(!reference.IsNull&&!Gone(reference),"SECTION_SETTINGS_UNAVAILABLE","Section settings contain an unavailable persistent reference.");reactors.Add(H(reference));}
        var kinds=new[]{Db.SectionType.LiveSection,Db.SectionType.Section2d,Db.SectionType.Section3d};
        var geometries=new[]{Db.SectionGeometry.IntersectionBoundary,Db.SectionGeometry.IntersectionFill,Db.SectionGeometry.BackgroundGeometry,Db.SectionGeometry.ForegroundGeometry,Db.SectionGeometry.CurveTangencyLines};
        var modes=new List<object>();
        foreach(var kind in kinds)
        {
            cancel();using var sources=new Db.ObjectIdCollection();settings.GetSourceObjects(kind,sources);var sourceIds=new List<string>();var sourceBytes=new ModelInventoryBudget(cancel);
            foreach(Db.ObjectId source in sources){cancel();Require(!source.IsNull&&!Gone(source),"SECTION_SETTINGS_UNAVAILABLE","Section generation has an unavailable stored source reference.");sourceBytes.Add(H(source));sourceIds.Add(H(source));}
            var appearance=new List<object>();foreach(var geometry in geometries)
            {
                cancel();using var color=settings.Color(kind,geometry);
                var value=new{geometry=geometry.ToString(),visibility=settings.Visibility(kind,geometry),color=Color(color),layer=settings.Layer(kind,geometry),
                    linetype=settings.Linetype(kind,geometry),linetype_scale=NativeOwnedMetadata.Value(settings.LinetypeScale(kind,geometry)),lineweight=settings.LineWeight(kind,geometry).ToString(),plot_style=settings.PlotStyleName(kind,geometry),
                    face_transparency=settings.FaceTransparency(kind,geometry),edge_transparency=settings.EdgeTransparency(kind,geometry),hatch_visibility=settings.HatchVisibility(kind,geometry),
                    hatch_pattern_type=settings.GetHatchPatternType(kind,geometry).ToString(),hatch_pattern_name=settings.GetHatchPatternName(kind,geometry),hatch_angle=NativeOwnedMetadata.Value(settings.HatchAngle(kind,geometry)),
                    hatch_scale=NativeOwnedMetadata.Value(settings.HatchScale(kind,geometry)),hatch_spacing=NativeOwnedMetadata.Value(settings.HatchSpacing(kind,geometry)),hidden_line=settings.HiddenLine(kind,geometry),division_lines=settings.DivisionLines(kind,geometry)};
                bytes.Add(value);appearance.Add(value);
            }
            var mode=new{kind=kind.ToString(),generation=settings.GenerationOptions(kind).ToString(),destination_block=Optional(settings.DestinationBlock(kind)),destination_file=settings.DestinationFile(kind),sources=sourceIds.ToArray(),appearance};
            bytes.Add(mode);modes.Add(mode);
        }
        return new{handle=H(id),owner=Optional(settings.OwnerId),type=settings.GetType().FullName,settings.CurrentSectionType,
            settings.HasFields,extension_handle=Optional(settings.ExtensionDictionary),owned=NativeOwnedMetadata.Capture(settings,tx,cancel),
            xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,bytes.Add),reactors=reactors.OrderBy(value=>value,StringComparer.Ordinal).ToArray(),modes};
    }
    internal static bool Same(JsonElement a,JsonElement b)=>SectionViewState.Same(a,b);
    static bool Point(JsonElement a,JsonElement b)=>a.ValueKind==JsonValueKind.Array&&b.ValueKind==JsonValueKind.Array&&a.GetArrayLength()==3&&b.GetArrayLength()==3&&
        a.EnumerateArray().Zip(b.EnumerateArray()).All(pair=>pair.First.TryGetDouble(out var left)&&pair.Second.TryGetDouble(out var right)&&double.IsFinite(left)&&double.IsFinite(right)&&Math.Abs(left-right)<=1e-8);
    static bool Points(JsonElement a,JsonElement b)=>a.ValueKind==JsonValueKind.Array&&b.ValueKind==JsonValueKind.Array&&a.GetArrayLength()==b.GetArrayLength()&&
        a.EnumerateArray().Zip(b.EnumerateArray()).All(pair=>Point(pair.First,pair.Second));
    internal static SectionState Section(Section section,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var document=Context(state);cancel();var raw=section.ID.Handle;Db.ObjectId id=Db.ObjectId.Null;
        Require(raw!=0&&state.Document.Database.TryGetObjectId(new Db.Handle(raw),out id)&&!id.IsNull&&!Gone(id)&&
            McObjectId.FromHandle(id.Handle.Value,document.ID)==section.ID,"OBJECT_IDENTITY_MISMATCH","The Section has no exact current resident drawing representation.");
        using var sectionBinding=SectionNativeBinding357.Section(tx.GetObject(id,Db.OpenMode.ForRead),state.Document.Database,cancel);var native=sectionBinding.Value;
        var owner=tx.GetObject(native.OwnerId,Db.OpenMode.ForRead) as Db.BlockTableRecord;
        Require(owner!=null&&owner.IsLayout&&owner.OwnerId==state.Document.Database.BlockTableId&&owner.Cast<Db.ObjectId>().Contains(id)&&
            !owner.IsDependent&&!owner.IsFromExternalReference&&!owner.IsFromOverlayReference,"OBJECT_IDENTITY_MISMATCH","Section ownership is not a retained local layout space.");
        var layer=(Db.LayerTableRecord)tx.GetObject(native.LayerId,Db.OpenMode.ForRead);
        Require(!layer.IsLocked&&!layer.IsDependent,"LAYER_UNAVAILABLE","The Section layer must be local and unlocked.");
        // Boundary is the SDK current cached collection over this resident Section;
        // Count/index read the same native vertices without a transferred owned wrapper.
        var boundary=native.Boundary;
        Require(section.ID.ToString().Length>0&&section.Status.ToString()=="Ok"&&!section.IsSuppressed&&
            section.State.ToString()=="Plane"&&!section.LiveSectionEnabled&&section.AssociatedPlanarObjectID.IsNull&&boundary!=null&&boundary.Count==2,
            "OUT_OF_SCOPE","Use a healthy non-live independent two-point Plane section; support-driven, jogged, boundary or volume sections need their owning geometry adapter.");
        var metadata=SectionViewState.Read(section,cancel);var vertices=metadata.GetProperty("vertices");
        Require(vertices.GetArrayLength()==2,"OUT_OF_SCOPE","The actual retained Plane section must report two cut-line vertices.");
        for(int i=0;i<2;i++)
        {cancel();var vertex=boundary[i];Require(Point(vertices[i],Wire.Element(new[]{vertex.X,vertex.Y,vertex.Z})),"OBJECT_IDENTITY_MISMATCH","Native Section representation and model cut vertices disagree.");}
        var owned=NativeOwnedMetadata.Capture(native,tx,cancel);using var buffer=native.XData;using var reactors=native.GetPersistentReactorIds();var bytes=new ModelInventoryBudget(cancel);
        var links=new List<string>();foreach(Db.ObjectId reference in reactors){cancel();Require(!reference.IsNull&&!Gone(reference),"SECTION_DEPENDENTS","A Section persistent reference is unavailable.");links.Add(H(reference));}
        using var indicator=native.IndicatorFillColor;
        var root=Wire.Element(new{handle=H(id),owner=H(native.OwnerId),type=native.GetType().FullName,properties=EntityAdapter.StoredProperties(native),
            native.HasFields,extension_handle=native.ExtensionDictionary.IsNull?null:H(native.ExtensionDictionary),owned,
            name=native.Name,state=native.State.ToString(),live=native.IsLiveSectionEnabled,indicator=Color(indicator),native.IndicatorTransparency,settings=Settings(native,tx,cancel),
            xdata=buffer==null?null:NativeOwnedMetadata.Buffer(buffer,cancel,bytes.Add),reactors=links.OrderBy(value=>value,StringComparer.Ordinal).ToArray()});
        CadDrawingViewLifecycleState345.Bytes(new{metadata,root},cancel);
        string token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{metadata,root},Wire.Json)));
        return new(section,id,metadata,root,token);
    }
    internal static (SectionCutRead351? Value,string? Reason) Describe(Section section,DocumentState state,Db.Transaction tx,string instance,Action cancel)
    {
        try{var value=Section(section,state,tx,cancel);return(new(new(instance,state.Id,H(value.Entity)),section.ID.ToString(),value.Token,"resident_two_point_native_plane_section_complete_root_state"),null);}
        catch(DomainException error)when(error.Error.code is not ("RESPONSE_LIMIT_EXCEEDED" or "RESPONSE_TOO_LARGE")){return(null,error.Error.code+": "+error.Error.message);}
        catch(Teigha.Runtime.Exception error){cancel();return(null,"Native Section cut metadata unavailable: "+error.ErrorStatus);}
        catch(NotSupportedException){cancel();return(null,"Native Section cut representation is unavailable.");}
    }
    internal static Plan Prepare(StructureWriteArguments args,DocumentState state,Db.Transaction tx,Action cancel,bool admission=true)
    {
        Require(args.Operations is [SetSectionViewCut351],"INVALID_OPERATION_ORDER","Supply one retained Section cut edit.");
        Require(args.Units=="Millimeters"&&(int)state.Document.Database.Insunits==4,"UNIT_CONTEXT_MISMATCH","Section WCS cut points require Millimeters in this named drawing.");
        var edit=(SetSectionViewCut351)args.Operations[0];var snapshot=CadDrawingViewLifecycleState345.Capture(state,tx,cancel);Tree(snapshot,cancel);
        var views=edit.Targets.Select(target=>admission?CadDrawingViewLifecycleState345.Resolve(target,snapshot,tx,cancel):CadDrawingViewEditState.Resolve(target.Target,snapshot,tx,cancel)).ToArray();var selected=views.Select(view=>view.Id).ToHashSet();
        var related=snapshot.Views.Where(view=>view.Native.SectionID.ToString().Equals(edit.SectionId,StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(related.Length>0,"SECTION_NOT_FOUND","No current native view references the exact section_id.");
        var section=related[0].Native.SectionID.GetObjectOfType<Section>();Require(section!=null&&section.ID==related[0].Native.SectionID,"OBJECT_IDENTITY_MISMATCH","Section lookup returned another native object.");
        var value=Section(section,state,tx,cancel);if(admission)Require(value.Token==edit.SectionToken,"PRECONDITION_FAILED","Section state changed; refresh its section_cut token.");
        var direct=section.GetViews();Require(direct!=null&&direct.ToHashSet().SetEquals(related.Select(view=>view.Id)),"SECTION_DEPENDENTS","Section/view backlinks do not identify the same complete associated group.");
        var affected=related.Select(view=>view.Id).ToHashSet();var stack=new Stack<View>(related);
        while(stack.Count>0){cancel();foreach(var child in stack.Pop().Native.ChildViews){cancel();if(affected.Add(child))stack.Push(snapshot.Views.Single(view=>view.Id==child));}}
        Require(affected.SetEquals(selected),"SECTION_DEPENDENTS","Explicitly select every associated Section view and descendant, and no unrelated view.");
        var dependencies=section.GetAllDependent(false);Require(dependencies!=null&&dependencies.All(selected.Contains),"SECTION_DEPENDENTS","Section has a native dependent outside the selected view group.");
        var owned=new Dictionary<McObjectId,JsonElement>();var allowed=new HashSet<Db.ObjectId>{value.Entity};foreach(var view in views)allowed.Add(view.Entity);
        void Reactors(Db.Entity entity)
        {using var links=entity.GetPersistentReactorIds();foreach(Db.ObjectId id in links){cancel();Require(allowed.Contains(id),"SECTION_DEPENDENTS","An affected root has an external persistent association requiring its owning cut-edit route.");}}
        Reactors((Db.Entity)tx.GetObject(value.Entity,Db.OpenMode.ForRead));
        foreach(var view in views)
        {
            cancel();Placement(view);Require(!view.Native.DependsOnSlice&&!view.Native.IsSlice,"OUT_OF_SCOPE","Slice-derived followers require their own cut adapter.");
            var refs=CadDrawingViewLifecycleState345.SourceRefs(view.Native.GetTop3dEntitiesInView(),state.Document.Database,tx,args.BridgeId,args.DocumentId,cancel);
            Require(refs.Refs is {Length:>0},"DRAWING_VIEW_SOURCE_UNAVAILABLE","Each retained view needs its exact addressable Model source set.");
            var dependents=view.Native.GetAllDependent(false);Require(dependents!=null&&dependents.All(selected.Contains)&&view.Native.DbEntity.ChildrenIds.All(selected.Contains),
                "SECTION_DEPENDENTS","View has an unselected native follower or owned child.");
            var entity=(Db.Entity)tx.GetObject(view.Entity,Db.OpenMode.ForRead);Reactors(entity);owned.Add(view.Id,CadDrawingViewLifecycleState345.Owned(view,tx,cancel));
        }
        var up=section.VerticalDirection;var normal=section.ViewingDirection;var along=up.CrossProduct(normal);var first=edit.SectionLine[0];var second=edit.SectionLine[1];
        var delta=new Multicad.Geometry.Vector3d(second.X-first.X,second.Y-first.Y,second.Z-first.Z);
        Require(Math.Abs(up.Length-1)<=1e-8&&Math.Abs(normal.Length-1)<=1e-8&&Math.Abs(up.DotProduct(normal))<=1e-8&&delta.DotProduct(along)>=.001&&
            Math.Abs(delta.DotProduct(up))<=1e-8&&Math.Abs(delta.DotProduct(normal))<=1e-8,"INVALID_GEOMETRY","Cut line must retain viewing/vertical basis and follow vertical cross viewing for at least0.001mm.");
        if(admission)Require(!Points(value.Metadata.GetProperty("vertices"),Wire.Element(edit.SectionLine.Select(p=>new[]{p.X,p.Y,p.Z}).ToArray())),"NO_CHANGE","The native Section line is already at the requested position.");
        return new(edit,snapshot,value,views,owned);
    }
    internal static void Verify(Plan before,Plan after,Action cancel)
    {
        Require(before.Section.Entity==after.Section.Entity&&before.Section.Native.ID==after.Section.Native.ID&&Same(before.Section.Owned,after.Section.Owned),"SECTION_UPDATE_MISMATCH","Section identity, owner, display or owned data changed.");
        Require(Points(after.Section.Metadata.GetProperty("vertices"),Wire.Element(before.Edit.SectionLine.Select(p=>new[]{p.X,p.Y,p.Z}).ToArray())),"SECTION_UPDATE_MISMATCH","Native cut vertices differ from the requested WCS points.");
        var expected=JsonNode.Parse(before.Section.Metadata.GetRawText())!.AsObject();expected["vertices"]=JsonNode.Parse(after.Section.Metadata.GetProperty("vertices").GetRawText());
        // SDK processing IDs depend on cut configuration; they are not the
        // explicit source ownership of each retained drawing view.
        expected["top_source_ids"]=JsonNode.Parse(after.Section.Metadata.GetProperty("top_source_ids").GetRawText());
        Require(Same(Wire.Element(expected),after.Section.Metadata),"SECTION_UPDATE_MISMATCH","Unrequested Section direction/state/support/backlinks changed.");
        Require(Same(before.Drawing.Context,after.Drawing.Context)&&Same(before.Drawing.Layouts,after.Drawing.Layouts)&&Same(before.Drawing.Entities,after.Drawing.Entities)&&
            before.Drawing.Views.Length==after.Drawing.Views.Length&&before.Drawing.Bodies.Length==after.Drawing.Bodies.Length,"SECTION_UPDATE_MISMATCH","Cut editing changed drawing/layout/entity membership or stored appearance.");
        foreach(var view in before.Drawing.Views)
        {cancel();var current=after.Drawing.Views.SingleOrDefault(item=>item.Id==view.Id);Require(current!=null&&current.Entity==view.Entity&&current.Layout==view.Layout&&current.Space==view.Space&&Same(view.Metadata,current.Metadata),"SECTION_UPDATE_MISMATCH","View identity, source/tree/history, plane/scale/settings changed.");}
        foreach(var view in before.Views){cancel();Require(after.Owned.TryGetValue(view.Id,out var owned)&&Same(before.Owned[view.Id],owned),"SECTION_UPDATE_MISMATCH","A retained view lost root/owned data.");}
        foreach(var body in before.Drawing.Bodies)
        {cancel();var current=after.Drawing.Bodies.SingleOrDefault(item=>item.Entity==body.Entity);Require(current!=null&&current.Id==body.Id&&current.Last==body.Last&&current.Contents.ToHashSet().SetEquals(body.Contents)&&Same(body.Nodes,current.Nodes)&&Same(body.Geometry,current.Geometry),"SECTION_UPDATE_MISMATCH","Cut editing changed a source body or retained feature history.");}
        var old=before.Drawing.Sections.EnumerateArray().ToDictionary(value=>value.GetProperty("id").GetString()!,StringComparer.Ordinal);
        foreach(var current in after.Drawing.Sections.EnumerateArray())
        {cancel();string id=current.GetProperty("id").GetString()!;if(id==before.Section.Native.ID.ToString())continue;Require(old.TryGetValue(id,out var previous)&&Same(previous,current),"SECTION_UPDATE_MISMATCH","An unrelated Section changed.");}
        Require(before.Drawing.Sections.GetArrayLength()==after.Drawing.Sections.GetArrayLength(),"SECTION_UPDATE_MISMATCH","Section inventory changed.");
    }
}
