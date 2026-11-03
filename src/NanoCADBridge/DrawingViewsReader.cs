using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using G=Multicad.Geometry;

namespace NanoCADBridge;

internal static class DrawingViewsReader
{
    private static object ReadGeometry(Multicad.Mc3D.DrawingView view,Entity entity,string instance,string document,int start,int pageSize,Action budget)
    {
        budget();
        var geometry=view.GetViewGeometry(true,0);
        if(geometry==null||geometry.Count==0)
            throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE","Selected native drawing view has no projected geometry.");
        try
        {
            if(start>geometry.Count)throw new DomainException("INVALID_ARGUMENT","geometry_start_index exceeds the selected projection inventory; refresh the current view/revision.");
            var items=new List<object>();bool complete=true;long bytes=2048;
            int end=start+Math.Min(pageSize,geometry.Count-start);
            for(int index=start;index<end;index++)
            {
                budget();var item=geometry[index];object? detail=null;
                if(item?.GeometryType==G.EntityGeomType.kLine)
                {using var line=item.LineSeg;detail=SketchGeometryReader.Line(line);}
                else if(item?.GeometryType==G.EntityGeomType.kCircArc)
                {using var arc=item.CircArc;detail=SketchGeometryReader.Arc(arc);}
                var entry=new{index,native_geometry_type=item?.GeometryType.ToString(),
                    status=detail==null?"unsupported_geometry":"read",geometry=detail,
                    unavailable_reason=detail==null?"native_geometry_type_not_serialized":(string?)null};
                int size=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entry,Wire.Json).Length+1;
                if(bytes+size>Wire.PayloadBudget-8192)
                {
                    if(items.Count==0)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","One native projected element exceeds the actual serialized-byte budget.");
                    break;
                }
                bytes+=size;items.Add(entry);if(detail==null)complete=false;
            }
            return new{view_ref=new EntityRef(instance,document,entity.Handle.ToString()),
                coordinate_system="WCS",unit="drawing_units",geometry_details_complete=complete,
                element_identity="projection_order_only_not_persistent_subentity_ids",geometry_scope="requested_projection_page",
                total_elements=geometry.Count,start_index=start,next_index=start+items.Count<geometry.Count?(int?)(start+items.Count):null,
                complete=start+items.Count==geometry.Count,items};
        }
        finally
        {
            foreach(var item in geometry.Distinct(ReferenceEqualityComparer.Instance))
                if(item is IDisposable disposable)disposable.Dispose();
        }
    }
    public static object Read(Arguments args,DocumentState state,Transaction tx,string instance,CancellationToken cancellation)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!loaded.Contains("mapimgd") || !loaded.Contains("MapiBaseTypes"))throw new DomainException("CAPABILITY_UNAVAILABLE","MultiCAD runtime is not already loaded.");
        var db=state.Document.Database;
        long bits=unchecked((long)ulong.Parse(args.Handles![0],NumberStyles.HexNumber,CultureInfo.InvariantCulture));
        if(!db.TryGetObjectId(new Handle(bits),out var id) || id.IsNull || !id.IsValid)throw new DomainException("OBJECT_NOT_FOUND","Layout not found.");
        if(id.IsErased || id.IsEffectivelyErased)throw new DomainException("OBJECT_ERASED","Layout erased.");
        if(tx.GetObject(id,OpenMode.ForRead) is not Layout layout || layout.OwnerId!=db.LayoutDictionaryId)
            throw new DomainException("OUT_OF_SCOPE","layout_handle must identify a layout in the requested drawing.");
        var block=(BlockTableRecord)tx.GetObject(layout.BlockTableRecordId,OpenMode.ForRead);
        if(!block.IsLayout || block.OwnerId!=db.BlockTableId)throw new DomainException("OUT_OF_SCOPE","Invalid layout block record.");
        return ReadMultiCad(args,state,tx,layout,block,instance,cancellation);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object ReadMultiCad(Arguments args,DocumentState state,Transaction tx,Layout layout,BlockTableRecord block,string instance,CancellationToken cancellation)
    {
        var watch=Stopwatch.StartNew();int scanned=0;
        void Budget(){cancellation.ThrowIfCancellationRequested();}
        var document=Multicad.DatabaseServices.McDocument.ActiveDocument;
        if(document==null || !state.Document.IsNamedDrawing || !string.Equals(Path.GetFullPath(document.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH","MultiCAD active document differs from the requested document.");
        string? Id(Multicad.McObjectId id)=>id.IsNull?null:id.ToString();
        string[]? Ids(ICollection<Multicad.McObjectId>? ids)
        {
            if(ids==null)return null;
            
            return ids.Select(x=>x.ToString()).ToArray();
        }
        double[] Vector(Multicad.Geometry.Vector3d p)=>new[]{Finite(p.X),Finite(p.Y),Finite(p.Z)};
        double[] Point(Multicad.Geometry.Point3d p)=>new[]{Finite(p.X),Finite(p.Y),Finite(p.Z)};
        object Plane(Multicad.Geometry.Plane3d plane)=>new{origin=Point(plane.Origin),axis_u=Vector(plane.DirU),axis_v=Vector(plane.DirV),normal=Vector(plane.Normal)};
        var items=new List<object>();
        var found=new List<(Entity Entity,Multicad.McObjectId Id,Multicad.Mc3D.DrawingView View)>();
        var layoutHandles=new Dictionary<Multicad.McObjectId,List<string>>();
        foreach(ObjectId nativeId in block)
        {
            Budget();scanned++;
            if(nativeId.IsErased || nativeId.IsEffectivelyErased || nativeId.ObjectClass.DxfName.Equals("3DSOLID",StringComparison.OrdinalIgnoreCase))continue;
            if(tx.GetObject(nativeId,OpenMode.ForRead) is not Entity entity || entity.OwnerId!=block.ObjectId)
                throw new DomainException("OBJECT_READ_FAILED","Invalid layout member.");
            var id=Multicad.McObjectId.FromHandle(entity.Handle.Value,document.ID);
            if(id.IsNull)continue;
            if(!layoutHandles.TryGetValue(id,out var handles)){handles=new List<string>();layoutHandles.Add(id,handles);}
            handles.Add(entity.Handle.ToString());
            var view=id.GetObjectOfType<Multicad.Mc3D.DrawingView>();
            if(view==null)continue;
            if(view.ID!=id)throw new DomainException("OBJECT_IDENTITY_MISMATCH","DrawingView lookup returned a different identity.");
            
            found.Add((entity,id,view));
        }
        foreach(var entry in found)
        {
            Budget();var entity=entry.Entity;var id=entry.Id;var view=entry.View;
            var children=Ids(view.ChildViews)??Array.Empty<string>();
            var sources=Ids(view.GetTop3dEntitiesInView());Budget();
            var freshness=CadDrawingViewLifecycleState345.TryToken(view,entity,layout.ObjectId,tx,Budget);
            var sourceRefs=CadDrawingViewLifecycleState345.SourceRefs(view.GetTop3dEntitiesInView(),state.Document.Database,tx,instance,state.Id,Budget);
            var attached=view.Attached2dBreaks;
            if(attached==null)throw new DomainException("MODEL_STATE_UNAVAILABLE","Native break inventory is unavailable.");
            
            var breakData=DrawingViewBreakReader.Read(attached,id,layoutHandles,instance,state.Id,Budget);
            object? sectionData=null;SectionCutRead351? sectionCut=null;string? sectionCutReason=null;
            if(!view.SectionID.IsNull)
            {
                var section=view.SectionID.GetObjectOfType<Multicad.Mc3D.Section>();
                if(section==null || section.ID!=view.SectionID)throw new DomainException("OBJECT_IDENTITY_MISMATCH","View section identity unavailable.");
                var metadata=SectionViewState.Read(section,Budget);
                
                
                sectionData=metadata;
                (sectionCut,sectionCutReason)=CadSectionCutState351.Describe(section,state,tx,instance,Budget);
            }
            items.Add(new{@ref=new EntityRef(instance,state.Id,entity.Handle.ToString()),mc_id=id.ToString(),name=Text(view.Name),
                status=view.Status.ToString(),suppressed=view.IsSuppressed,direction=view.DirectionType.ToString(),
                native_owner_document_id=Id(view.DbEntity.DocumentID),
                section_id=Id(view.SectionID),section=sectionData,section_cut=sectionCut,section_cut_unavailable_reason=sectionCutReason,parent_view_id=Id(view.ParentViewID),root_view_id=Id(view.RootViewID),child_view_ids=children,
                attached_break_ids=attached.Select(i=>i.ToString()).ToArray(),breaks=breakData.Items,breaks_unavailable_reason=breakData.UnavailableReason,
                top_source_ids=sources,source_inventory_available=sources!=null,view_plane=Plane(view.ViewPlane),projection_plane=Plane(view.ProjPlane),
                view_token=freshness.Token,view_token_unavailable_reason=freshness.Reason,
                source_refs=sourceRefs.Refs,source_refs_unavailable_reason=sourceRefs.Reason,
                model_scale=Finite(view.DbEntity.DScale),design_scale=Finite(view.DbEntity.Scale),
                is_slice=view.IsSlice,is_based_on_section=view.IsBasedOnSection,is_root_section_view=view.IsRootSectionView,
                depends_on_slice=view.DependsOnSlice,auto_update_enabled=view.EnableAutoUpdate,
                align_on_parent=view.AlignOnParent,attached_local_cut_ids=view.AttachedLocalCuts?.Select(cut=>cut.ToString()).ToArray(),outer_cut_sketch_id=Id(view.OuterCutSketch),
                show_hidden_lines=view.ShowHiddenLines,show_hatches=view.ShowHatches,show_tangent_edges=view.ShowTangentEdges});
        }
        object? selectedGeometry=null;
        if(args.GeometryViewHandle!=null)
        {
            var selected=found.SingleOrDefault(x=>Validation.NormalizeHandle(x.Entity.Handle.ToString())==args.GeometryViewHandle);
            if(selected.View==null)throw new DomainException("OUT_OF_SCOPE","geometry_view_handle is not a native drawing view in the requested layout.");
            selectedGeometry=ReadGeometry(selected.View,selected.Entity,instance,state.Id,args.GeometryStartIndex,args.GeometryPageSize,Budget);
        }
        Budget();
        var result=new{layout_ref=new EntityRef(instance,state.Id,args.Handles![0]),layout_name=Text(layout.LayoutName),space=layout.ModelType?"model":"paper",
            identity_scope="bridge_document_revision",reference_scope="opaque_native_ids_no_external_traversal",coordinate_system="native_view_planes",
            geometry_state=selectedGeometry==null?"stored_metadata_not_regenerated":"selected_native_projection_without_regeneration",
            selected_geometry=selectedGeometry,scanned_entities=scanned,complete=true,items};
        if(args.DrawingViewSourceIdentityHandle is {} sourceHandle)
        {
            var selected=found.SingleOrDefault(item=>Validation.NormalizeHandle(item.Entity.Handle.ToString())==sourceHandle);
            if(selected.View==null)throw new DomainException("OUT_OF_SCOPE","source_identity_view_handle is not a native view in the explicitly selected layout.");
            SectionSourceSelectionRead357? selection=null;
            if(!selected.View.SectionID.IsNull)
            {
                var section=selected.View.SectionID.GetObjectOfType<Multicad.Mc3D.Section>();
                if(section==null||section.ID!=selected.View.SectionID)throw new DomainException("OBJECT_IDENTITY_MISMATCH","Selected view Section identity is unavailable.");
                selection=SectionSourceSelection357.Read(section,state,tx,Budget);
            }
            var detail=new{view_ref=new EntityRef(instance,state.Id,selected.Entity.Handle.ToString()),identity=DrawingViewSources356.Capture(selected.View,Budget),section_source_selection=selection};
            var output=System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(result,Wire.Json))!.AsObject();
            output["selected_source_identity"]=System.Text.Json.JsonSerializer.SerializeToNode(detail,Wire.Json);
            var expanded=Wire.Element(output);CadDrawingViewLifecycleState345.Bytes(expanded,Budget,Wire.PayloadBudget-8192);return expanded;
        }
        CadDrawingViewLifecycleState345.Bytes(result,Budget,Wire.PayloadBudget-8192);return result;
    }
    private static string Text(string value)=>System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length<=Wire.PayloadBudget-8192?value:
        throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Drawing view text exceeds the actual serialized-byte budget.");
    private static double Finite(double value)=>double.IsFinite(value)?value:throw new DomainException("INVALID_GEOMETRY","Non-finite drawing view metadata.");
}
