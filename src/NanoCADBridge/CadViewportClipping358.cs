using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Native clipping can reframe the resident viewport to its boundary. Preserve
// the Model-to-Paper map and scale by compensating its derived view coordinates.
// No clone, erase, reparent, owner reset, boundary transform or sheet transfer.
internal static class CadViewportClipping358
{
    internal const string Warning="VIEWPORT_CLIPPING_OBSERVATION: Native clipping may reframe the Paper viewport to the boundary extents. Model-to-Paper placement, scale and viewing basis are retained through verified derived view-height/center compensation. Native links, boundary curves, owning sheet membership/appearance and target/boundary readable metadata are verified. Unrelated sheet private data and opaque native/field internals are not exhaustively observable. Clipping changes visibility, not Model geometry or annotation association; inspect the rendered sheet detail.";
    internal sealed record PaperFrame(double X,double Y,double Z,double Width,double Height);
    internal sealed record Boundary(ObjectId Id,ObjectId Reverse,JsonElement Shape,JsonElement Metadata,JsonElement Appearance,string CurveToken,PaperFrame Frame);
    internal sealed record SheetEntity(ObjectId Id,JsonElement Appearance,ViewportProperties? Viewport);
    internal sealed record Snapshot(ObjectId Layout,ObjectId Block,ObjectId Overview,ObjectId Viewport,EntityRef Context,
        string Name,LayoutProperties LayoutState,ViewportProperties View,JsonElement Metadata,
        JsonElement Appearance,Boundary? Clip,SheetEntity[] Entities,string Token);
    internal sealed record Resolved(SetViewportClipping358 Edit,Snapshot Before,Boundary? Target,ObjectId ExpectedBoundary,bool ExpectedEnabled);
    static void Need([DoesNotReturnIf(false)] bool value,string message,string code="VIEWPORT_CLIPPING_STATE_CHANGED")
    {if(!value)throw new DomainException(code,message);}
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static bool DatabaseMatches(DBObject value,Database db)=>db.UnmanagedObject!=IntPtr.Zero&&value.Database?.UnmanagedObject==db.UnmanagedObject;
    static EntityRef Ref(EntityRef context,ObjectId id)=>context with{handle=Validation.NormalizeHandle(id.Handle.ToString())};
    static JsonElement Appearance(Entity value)=>Wire.Element(new{type=value.GetType().FullName,
        layer=Validation.NormalizeHandle(value.LayerId.Handle.ToString()),linetype=Validation.NormalizeHandle(value.LinetypeId.Handle.ToString()),
        properties=EntityAdapter.StoredProperties(value)});
    static string Digest(object value,Action cancel)
    {cancel();var bytes=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json);cancel();Need(bytes.Length<=8*1024*1024,"Complete clipping snapshot exceeds the actual 8 MiB admission budget.","RESPONSE_LIMIT_EXCEEDED");return Convert.ToHexString(SHA256.HashData(bytes));}
    static void Same(JsonElement before,JsonElement after,string message,Action cancel)
    {cancel();Need(before.GetRawText()==after.GetRawText(),message,"VIEWPORT_CLIPPING_VERIFICATION_FAILED");}
    static ObjectId Reverse(ObjectId boundary,Action cancel)
    {cancel();var result=LayoutManager.Current.GetNonRectangularViewportIdFromClipId(boundary);cancel();return result;}
    // Only floating-point evaluation error in the coordinate/ratio formulas
    // is normalized. This is not a tolerance for an incorrect drawing size.
    static bool Near(double expected,double actual)=>double.IsFinite(expected)&&double.IsFinite(actual)&&
        Math.Abs(expected-actual)<=64*2.2204460492503131e-16*Math.Max(1,Math.Max(Math.Abs(expected),Math.Abs(actual)));
    static PaperFrame Frame(ViewportProperties view)=>new(view.center[0],view.center[1],view.center[2],view.width,view.height);
    static PaperFrame Frame(Viewport native)=>new(native.CenterPoint.X,native.CenterPoint.Y,native.CenterPoint.Z,native.Width,native.Height);
    static bool SameFrame(PaperFrame expected,PaperFrame actual)=>Near(expected.X,actual.X)&&Near(expected.Y,actual.Y)&&
        Near(expected.Z,actual.Z)&&Near(expected.Width,actual.Width)&&Near(expected.Height,actual.Height);
    static void RequireFrame(Resolved value,PaperFrame actual)
    {
        Need(double.IsFinite(actual.Width)&&double.IsFinite(actual.Height)&&actual.Width>0&&actual.Height>0,
            "Native clipping produced an invalid Paper frame.","VIEWPORT_CLIPPING_VERIFICATION_FAILED");
        var before=Frame(value.Before.View);
        // Enabled clipping uses its boundary's native Paper extents. Linking a
        // disabled boundary may already reframe it; both specified native states
        // preserve the same map. Clearing retains the last rectangular frame.
        bool valid=value.ExpectedBoundary.IsNull?SameFrame(before,actual):
            SameFrame(value.Target!.Frame,actual)||!value.ExpectedEnabled&&SameFrame(before,actual);
        if(!valid)throw new DomainException("VIEWPORT_CLIPPING_VERIFICATION_FAILED",
            "Native clipping frame is neither the requested boundary extents nor a retained disabled frame.",false,
            new{action=value.Edit.Action,enabled=value.ExpectedEnabled,before,boundary=value.Target?.Frame,actual});
    }
    static (double Height,Point2d Center) Camera(ViewportProperties before,PaperFrame frame)
    {
        double scale=before.custom_scale;
        // In the unchanged view DCS basis, paper = frame center + scale *
        // (projected Model point - ViewCenter). Preserve both affine terms.
        double height=frame.Height/scale;
        var center=new Point2d(before.view_center[0]+(frame.X-before.center[0])/scale,
            before.view_center[1]+(frame.Y-before.center[1])/scale);
        Need(double.IsFinite(height)&&height>0&&double.IsFinite(center.X)&&double.IsFinite(center.Y),
            "Clipping frame compensation cannot represent the retained Model-to-Paper mapping.","VIEWPORT_CLIPPING_VERIFICATION_FAILED");
        return(height,center);
    }
    static void PreserveView(Resolved value,ViewportProperties actual)
    {
        var before=value.Before.View;var frame=Frame(actual);RequireFrame(value,frame);var camera=Camera(before,frame);
        Need(Near(before.custom_scale,actual.custom_scale)&&Near(frame.Height/actual.view_height,actual.custom_scale)&&
            Near(camera.Height,actual.view_height)&&Near(camera.Center.X,actual.view_center[0])&&Near(camera.Center.Y,actual.view_center[1])&&
            actual.standard_scale_native_value is >=1 and <=34,
            "Native clipping did not retain the Model-to-Paper placement and scale.","VIEWPORT_CLIPPING_VERIFICATION_FAILED");
        // Frame/view coordinates are admitted only after geometry/map proof.
        // Remaining native camera basis, lock/display, layers and links stay exact.
        var expected=before with{center=actual.center,width=actual.width,height=actual.height,view_height=actual.view_height,
            view_center=actual.view_center,custom_scale=actual.custom_scale,standard_scale_native_value=actual.standard_scale_native_value,
            non_rect_clip_on=value.ExpectedEnabled,clip_boundary=actual.clip_boundary};
        ViewportWriteValidation.RequireSame(expected,actual);
    }
    static Boundary ReadBoundary(ObjectId id,ObjectId block,ObjectId viewport,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(Live(id),"Clipping boundary identity is unavailable.","CLIP_BOUNDARY_UNAVAILABLE");
        var value=tr.GetObject(id,OpenMode.ForRead);
        Need(value is Curve&&(value.GetType()==typeof(Circle)||value.GetType()==typeof(Ellipse)||value.GetType()==typeof(Polyline)),
            "Clipping link requires an exact native Circle, Ellipse or Polyline boundary adapter.","UNSUPPORTED_CLIP_BOUNDARY");
        Need(DatabaseMatches(value,db)&&value.OwnerId==block,"Clipping boundary belongs to a different sheet/database.","CLIP_BOUNDARY_NOT_IN_LAYOUT");
        var native=(Curve)value;Need(native.Closed&&native.IsPlanar,"Clipping boundary must be a closed planar native curve.","INVALID_CLIP_BOUNDARY");
        var shape=CadNativeCurveInspection342.Shape(native,cancel);var plane=shape.GetProperty("plane");
        Need(plane.ValueKind==JsonValueKind.Object,"Native clipping boundary plane is unavailable.","CLIP_BOUNDARY_UNAVAILABLE");
        var point=plane.GetProperty("point").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
        var normal=plane.GetProperty("normal").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
        Need(point.Length==3&&normal.Length==3&&point.All(double.IsFinite)&&normal.All(double.IsFinite)&&
            Math.Abs(point[2])<=1e-8&&Math.Abs(normal[0])<=1e-8&&Math.Abs(normal[1])<=1e-8&&Math.Abs(Math.Abs(normal[2])-1)<=1e-8,
            "Clipping boundary must lie in its ordinary Paper XY plane.","INVALID_CLIP_BOUNDARY");
        double area=native.Area;Need(double.IsFinite(area)&&Math.Abs(area)>0,"Clipping boundary has no finite nonzero native area.","INVALID_CLIP_BOUNDARY");
        cancel();var extents=native.GeometricExtents;var min=extents.MinPoint;var max=extents.MaxPoint;
        var frame=new PaperFrame(min.X+(max.X-min.X)/2,min.Y+(max.Y-min.Y)/2,0,max.X-min.X,max.Y-min.Y);
        Need(new[]{frame.X,frame.Y,frame.Width,frame.Height}.All(double.IsFinite)&&frame.Width>0&&frame.Height>0,
            "Clipping boundary has no finite nondegenerate native Paper extents.","INVALID_CLIP_BOUNDARY");
        ObjectId reverse=Reverse(id,cancel);Need(reverse.IsNull||reverse==viewport,"Clipping boundary is already linked to another viewport.","CLIP_BOUNDARY_IN_USE");
        return new(id,reverse,shape,CadNativeStyleIdentityMetadata352.Metadata(value,db,tr,cancel),Appearance(native),NativeCurveRead342.Token(shape,cancel),frame);
    }
    static Snapshot Capture(ObjectId layoutId,ObjectId viewportId,EntityRef context,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(Live(layoutId)&&Live(viewportId),"Viewport/layout identity is unavailable.");
        var raw=tr.GetObject(layoutId,OpenMode.ForRead);
        Need(raw is Layout layout&&raw.GetType()==typeof(Layout)&&DatabaseMatches(raw,db)&&raw.OwnerId==db.LayoutDictionaryId&&!layout.ModelType,
            "Expected an ordinary exact Paper layout in this drawing.","VIEWPORT_NOT_IN_LAYOUT");
        var sheet=(Layout)raw;var block=tr.GetObject(sheet.BlockTableRecordId,OpenMode.ForRead) as BlockTableRecord;
        Need(block!=null&&block.GetType()==typeof(BlockTableRecord)&&DatabaseMatches(block,db)&&block.IsLayout&&block.LayoutId==layoutId&&
            block.OwnerId==db.BlockTableId&&!block.IsDependent&&!block.IsFromExternalReference&&!block.IsFromOverlayReference,
            "Paper sheet owner/block affinity is inconsistent.","VIEWPORT_NOT_IN_LAYOUT");
        var viewport=tr.GetObject(viewportId,OpenMode.ForRead) as Viewport;
        Need(viewport!=null&&viewport.GetType()==typeof(Viewport)&&DatabaseMatches(viewport,db)&&viewport.OwnerId==block.ObjectId,
            "Viewport must be an exact resident member of the selected Paper sheet.","VIEWPORT_NOT_IN_LAYOUT");
        // Unrelated sheet private metadata is outside this clipping/map edit.
        // Prove membership, actual owners/database, type/display and other
        // viewport cameras without traversing opaque fields/owned graphs.
        var records=new List<SheetEntity>();var overviews=new List<ObjectId>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId id in block)
        {
            cancel();if(!Live(id))continue;var entity=tr.GetObject(id,OpenMode.ForRead) as Entity;
            Need(entity!=null&&entity.OwnerId==block.ObjectId&&DatabaseMatches(entity,db),"Sheet member has inconsistent native owner/database.");
            ViewportProperties? view=null;
            if(entity is Viewport item)
            {Need(item.GetType()==typeof(Viewport),"Custom viewport member requires its owning route.");view=LayoutMetadataReader.ReadViewport(item,sheet,tr,db,v=>Ref(context,v),cancel,cancel);if(view.number==1)overviews.Add(id);}
            var observed=new SheetEntity(id,Appearance(entity),view);
            budget.Add(new{handle=Ref(context,id).handle,observed.Appearance,observed.Viewport});records.Add(observed);
        }
        Need(records.Count(v=>v.Id==viewportId)==1&&overviews.Count==1,"Viewport membership or reserved overview identity is ambiguous.");
        var properties=LayoutMetadataReader.ReadViewport(viewport,sheet,tr,db,v=>Ref(context,v),cancel,cancel);
        var layoutState=LayoutMetadataReader.ReadLayout(sheet,tr,db,v=>Ref(context,v),cancel,cancel);
        var metadata=CadNativeStyleIdentityMetadata352.Metadata(viewport,db,tr,cancel);var appearance=Appearance(viewport);
        Boundary? boundary=viewport.NonRectClipEntityId.IsNull?null:ReadBoundary(viewport.NonRectClipEntityId,block.ObjectId,viewportId,db,tr,cancel);
        if(boundary!=null)Need(boundary.Reverse==viewportId,"Stored clipping ID and actual native reverse link disagree.","CLIP_LINK_UNRESOLVED");
        else Need(!properties.non_rect_clip_on,"Native clipping is enabled without a boundary.","CLIP_LINK_UNRESOLVED");
        // Generic metadata retains the complete readable typed XData, including
        // ACAD/MVIEW and other applications. Legacy MVIEW data is never written
        // directly; only verified derived coordinates can differ in readback.
        var token=Digest(new{context,layout=layoutState,viewport=properties,metadata,appearance,
            boundary=boundary==null?null:new{boundary.Shape,boundary.Metadata,boundary.Appearance,boundary.CurveToken,boundary.Frame,reverse=Ref(context,boundary.Reverse)},
            entities=records.Select(v=>new{handle=Ref(context,v.Id).handle,v.Appearance,v.Viewport})},cancel);
        return new(layoutId,block.ObjectId,overviews[0],viewportId,context,sheet.LayoutName,layoutState,properties,metadata,appearance,boundary,records.ToArray(),token);
    }
    static string? CandidateReason(Snapshot value,Database db,Transaction tr)
    {
        if(value.View.number==1)return "Reserved overview viewport1 is protected.";
        if(value.View.is_perspective||value.View.front_clip_on||value.View.back_clip_on)return "Perspective/front/back clipped viewport needs its owning route.";
        if(!Near(value.View.center[2],0)||!double.IsFinite(value.View.custom_scale)||value.View.custom_scale<=0||
            !Near(value.View.height/value.View.view_height,value.View.custom_scale))return "Viewport has no consistent ordinary Paper mapping/scale.";
        var viewport=(Viewport)tr.GetObject(value.Viewport,OpenMode.ForRead);var layer=tr.GetObject(viewport.LayerId,OpenMode.ForRead) as LayerTableRecord;
        if(layer==null||layer.GetType()!=typeof(LayerTableRecord)||layer.OwnerId!=db.LayerTableId||layer.IsDependent||layer.IsOff||layer.IsFrozen||layer.IsLocked)
            return "Viewport layer must be local, visible, thawed and unlocked.";
        return null;
    }
    internal static ViewportClippingRead358 Read(Viewport viewport,Layout layout,EntityRef context,Database db,Transaction tr,Action cancel)
    {
        var properties=LayoutMetadataReader.ReadViewport(viewport,layout,tr,db,v=>Ref(context,v),cancel,cancel);
        // Unsupported observation of this individual viewport is explicit. It
        // does not disable other ordinary viewport clipping candidates.
        try
        {
            var value=Capture(layout.ObjectId,viewport.ObjectId,context,db,tr,cancel);string? reason=CandidateReason(value,db,tr);
            return new(value.View,reason==null,reason==null?value.Token:null,value.Clip==null?null:Ref(context,value.Clip.Id),
                value.Clip?.CurveToken,value.Clip==null?null:Ref(context,value.Clip.Reverse),ViewportClippingValidation358.Scope,reason);
        }
        catch(DomainException error) when(error.Error.code is "UNSUPPORTED_VIEWPORT" or "UNSUPPORTED_CLIP_BOUNDARY" or "CLIP_BOUNDARY_UNAVAILABLE" or "CLIP_LINK_UNRESOLVED" or "INVALID_CLIP_BOUNDARY")
        {cancel();return new(properties,false,null,properties.clip_boundary.@ref,null,null,ViewportClippingValidation358.Scope,error.Error.message);}
    }
    static void Active(Snapshot value,Database db,Transaction tr,Action cancel)
    {
        cancel();var document=Application.DocumentManager.MdiActiveDocument;
        Need(document!=null&&document.Database.UnmanagedObject==db.UnmanagedObject&&!db.TileMode&&db.CurrentSpaceId==value.Block,
            "Activate this ordinary Paper sheet outside its model viewport.","LAYOUT_NOT_ACTIVE");
        object cvport=Application.GetSystemVariable("CVPORT");int number=cvport switch{short n=>n,int n=>n,_=>-1};
        Need(number==1&&document.Editor.CurrentViewportObjectId==value.Overview,"Native Paper overview CVPORT1 must remain active.","ACTIVE_VIEWPORT_PROTECTED");
        Need(LayoutManager.Current.GetLayoutId(LayoutManager.Current.CurrentLayout)==value.Layout,"Native active layout differs from the requested sheet.","LAYOUT_NOT_ACTIVE");
        Need(CandidateReason(value,db,tr)==null,"Viewport is not an ordinary writable clipping candidate.","UNSUPPORTED_VIEWPORT");
    }
    internal static Resolved Resolve(StructureWriteArguments args,SetViewportClipping358 edit,Database db,Transaction tr,Action cancel)
    {
        Need(args.EffectiveTargetSpace=="paper","Viewport clipping requires explicit target_space paper.","OUT_OF_SCOPE");
        var context=new EntityRef(args.BridgeId,args.DocumentId,edit.Handle);
        var before=Capture(CadStructureWriter.Find(db,edit.LayoutHandle),CadStructureWriter.Find(db,edit.Handle),context,db,tr,cancel);
        Active(before,db,tr,cancel);Need(before.Token==edit.ClippingToken,"Viewport clipping state changed; read a fresh token.");
        _=MappingMetadata(before.Metadata,before.View,cancel);
        Boundary? target=before.Clip;
        if(edit.Action=="set_boundary")
        {
            target=ReadBoundary(CadStructureWriter.Find(db,edit.BoundaryHandle!),before.Block,before.Viewport,db,tr,cancel);
            Need(target.CurveToken==edit.BoundaryCurveToken,"Requested boundary geometry changed; inspect its native curve token again.","CLIP_BOUNDARY_STATE_CHANGED");
        }
        if(edit.Action is "enable" or "disable")Need(target!=null,"A retained boundary is required for enable/disable.","CLIP_BOUNDARY_UNAVAILABLE");
        ObjectId expected=edit.Action=="clear"?ObjectId.Null:target?.Id??ObjectId.Null;
        bool enabled=edit.Action switch{"clear" or "disable"=>false,"enable"=>true,_=>edit.Enabled!.Value};
        Need(expected!=(before.Clip?.Id??ObjectId.Null)||enabled!=before.View.non_rect_clip_on,"Requested clipping state is already current.","NO_CHANGE");
        var result=new Resolved(edit,before,target,expected,enabled);
        new ModelInventoryBudget(cancel).Add(SourceObservation(result));return result;
    }
    internal static object SourceObservation(Resolved value)=>new{value.Before.LayoutState,value.Before.View,
        value.Before.Metadata,value.Before.Appearance,
        entities=value.Before.Entities.Select(v=>new{handle=Ref(value.Before.Context,v.Id).handle,v.Appearance,v.Viewport}),
        retained=value.Before.Clip==null?null:new{value.Before.Clip.Shape,value.Before.Clip.Metadata,value.Before.Clip.Appearance,value.Before.Clip.CurveToken,value.Before.Clip.Frame},
        target=value.Target==null?null:new{value.Target.Shape,value.Target.Metadata,value.Target.Appearance,value.Target.CurveToken,value.Target.Frame}};
    internal static ViewportClippingChange358 ProjectResult(int index,Resolved value)=>new(index,value.Before.Context,
        Ref(value.Before.Context,value.Before.Layout),value.Edit.Action,value.ExpectedBoundary.IsNull?null:Ref(value.Before.Context,value.ExpectedBoundary),
        value.ExpectedEnabled,ViewportClippingValidation358.Scope);
    internal static void Verify(Resolved value,Database db,Transaction tr,Action cancel)
    {
        var current=Capture(value.Before.Layout,value.Before.Viewport,value.Before.Context,db,tr,cancel);
        Active(current,db,tr,cancel);Need(current.Token==value.Before.Token,"Viewport/sheet changed before its clipping write.");
        if(value.Target!=null)
        {
            var target=ReadBoundary(value.Target.Id,value.Before.Block,value.Before.Viewport,db,tr,cancel);
            Same(value.Target.Shape,target.Shape,"Target boundary geometry changed before clipping.",cancel);
            Same(value.Target.Metadata,target.Metadata,"Target boundary readable metadata changed before clipping.",cancel);
            Same(value.Target.Appearance,target.Appearance,"Target boundary appearance changed before clipping.",cancel);
            Need(SameFrame(value.Target.Frame,target.Frame),"Target boundary native extents changed before clipping.");
            Need(value.Target.Reverse==target.Reverse,"Target boundary reverse link changed before clipping.");
        }
    }
    static JsonElement WithoutViewportReactor(JsonElement metadata,EntityRef viewport)
    {
        var result=JsonNode.Parse(metadata.GetRawText())!.AsObject();
        var values=result["reactors"]!.AsArray();
        result["reactors"]=new JsonArray(values.Where(v=>!string.Equals(v?.GetValue<string>(),viewport.handle,StringComparison.OrdinalIgnoreCase))
            .Select(v=>v==null?null:JsonNode.Parse(v.ToJsonString())).ToArray());return Wire.Element(result);
    }
    static JsonElement MappingMetadata(JsonElement metadata,ViewportProperties view,Action cancel)
    {
        double Number(JsonElement value)
        {
            Need(value.ValueKind==JsonValueKind.Object&&value.GetProperty("native_type").GetString()=="double",
                "Existing ACAD/MVIEW number has no readable native double representation.","UNSUPPORTED_VIEWPORT");
            ulong bits=ulong.Parse(value.GetProperty("bits").GetString()!,System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture);
            double decoded=BitConverter.Int64BitsToDouble(unchecked((long)bits));
            Need(double.IsFinite(decoded),"Existing ACAD/MVIEW number is nonfinite.","UNSUPPORTED_VIEWPORT");return decoded;
        }
        double[] Point(JsonElement value)
        {
            Need(value.ValueKind==JsonValueKind.Object&&value.GetProperty("native_type").GetString() is "Point3d" or "Vector3d",
                "Existing ACAD/MVIEW point has no readable native 3D representation.","UNSUPPORTED_VIEWPORT");
            return value.GetProperty("coordinates").EnumerateArray().Select(Number).ToArray();
        }
        var result=JsonNode.Parse(metadata.GetRawText())!.AsObject();
        if(result["xdata"] is not JsonArray data)return Wire.Element(result);
        for(int at=0;at<data.Count;)
        {
            cancel();var first=data[at]!.AsObject();
            if(first["code"]!.GetValue<int>()!=1001){at++;continue;}
            int end=at+1;while(end<data.Count&&data[end]!["code"]!.GetValue<int>()!=1001){cancel();end++;}
            if(first["value"]!.GetValue<string>()=="ACAD"&&at+1<end&&data[at+1]!["code"]!.GetValue<int>()==1000&&
                data[at+1]!["value"]!.GetValue<string>()=="MVIEW")
            {
                // Only the recognized existing legacy representation has these
                // three derived coordinates. Other applications/unknown ACAD
                // payloads, typed codes, headers and remaining values stay exact.
                var chunk=new List<ViewportDataValue>();
                for(int index=at;index<end;index++)
                {
                    cancel();Need(data[index] is JsonObject,"Existing ACAD/MVIEW element is not a typed native value.","UNSUPPORTED_VIEWPORT");
                    var item=Wire.Element(data[index]!);int code=item.GetProperty("code").GetInt32();var raw=item.GetProperty("value");
                    object value=code switch{
                        1000 or 1001 or 1002 or 1003=>raw.GetString()!,1070=>raw.GetInt32(),1040=>Number(raw),1010=>Point(raw),
                        _=>throw new DomainException("UNSUPPORTED_VIEWPORT","Existing ACAD/MVIEW typed representation is unsupported.")};
                    chunk.Add(new(code,value));
                }
                ViewportMviewData.Validate(chunk.ToArray(),cancel);
                var coordinates=new[]{(Index:7,Value:view.view_height),(Index:8,Value:view.view_center[0]),(Index:9,Value:view.view_center[1])};
                foreach(var coordinate in coordinates)
                {
                    cancel();Need(chunk[coordinate.Index].Value is double native&&Near(coordinate.Value,native),
                        "Legacy MVIEW coordinates disagree with the observed native clipping map.","VIEWPORT_CLIPPING_VERIFICATION_FAILED");
                    data[at+coordinate.Index]!["value"]=JsonValue.Create("verified_clipping_mapping_coordinate_"+coordinate.Index);
                }
            }
            at=end;
        }
        return Wire.Element(result);
    }
    static void PreserveBoundary(Boundary expected,Resolved value,Database db,Transaction tr,Action cancel)
    {
        var actual=ReadBoundary(expected.Id,value.Before.Block,value.Before.Viewport,db,tr,cancel);
        Same(expected.Shape,actual.Shape,"Native clipping changed retained boundary geometry.",cancel);
        Need(SameFrame(expected.Frame,actual.Frame),"Native clipping changed retained boundary extents.","VIEWPORT_CLIPPING_VERIFICATION_FAILED");
        Same(expected.Appearance,actual.Appearance,"Native clipping changed retained boundary appearance.",cancel);
        // Only this exact viewport's native association reactor may accompany
        // the verified forward/reverse link change; every other link stays.
        Same(WithoutViewportReactor(expected.Metadata,value.Before.Context),WithoutViewportReactor(actual.Metadata,value.Before.Context),
            "Native clipping changed boundary metadata or unrelated dependencies.",cancel);
        ObjectId reverse=expected.Id==value.ExpectedBoundary?value.Before.Viewport:ObjectId.Null;
        Need(actual.Reverse==reverse,"Actual boundary-to-viewport reverse association differs from the requested link.","VIEWPORT_CLIPPING_VERIFICATION_FAILED");
    }
    static void VerifyDesired(Resolved value,IReadOnlyDictionary<ObjectId,Resolved> group,Database db,Transaction tr,Action cancel)
    {
        var after=Capture(value.Before.Layout,value.Before.Viewport,value.Before.Context,db,tr,cancel);
        Active(after,db,tr,cancel);
        Need((after.Clip?.Id??ObjectId.Null)==value.ExpectedBoundary&&after.View.non_rect_clip_on==value.ExpectedEnabled,
            "Native clipping properties differ from the requested state.","VIEWPORT_CLIPPING_VERIFICATION_FAILED");
        PreserveView(value,after.View);
        LayoutCreateValidation.RequireSame(value.Before.LayoutState,after.LayoutState);
        Same(MappingMetadata(value.Before.Metadata,value.Before.View,cancel),MappingMetadata(after.Metadata,after.View,cancel),
            "Viewport root/readable owned metadata changed outside its verified derived clipping coordinates.",cancel);
        Same(value.Before.Appearance,after.Appearance,"Viewport appearance changed during clipping.",cancel);
        Need(value.Before.Entities.Select(v=>v.Id).ToHashSet().SetEquals(after.Entities.Select(v=>v.Id)),"Sheet membership changed during clipping.","VIEWPORT_CLIPPING_VERIFICATION_FAILED");
        var boundaries=new[]{value.Before.Clip,value.Target}.Where(v=>v!=null).Select(v=>v!).DistinctBy(v=>v.Id).ToArray();
        var groupBoundaries=group.Values.SelectMany(v=>new[]{v.Before.Clip,v.Target}).Where(v=>v!=null).Select(v=>v!.Id).ToHashSet();
        foreach(var boundary in boundaries){cancel();PreserveBoundary(boundary,value,db,tr,cancel);}
        foreach(var old in value.Before.Entities)
        {
            cancel();if(old.Id==value.Before.Viewport||groupBoundaries.Contains(old.Id))continue;
            var current=after.Entities.Single(v=>v.Id==old.Id);
            Same(old.Appearance,current.Appearance,"Unrelated sheet entity appearance changed.",cancel);
            if(old.Viewport!=null)
            {
                if(group.TryGetValue(old.Id,out var sibling))PreserveView(sibling,current.Viewport!);
                else ViewportWriteValidation.RequireSame(old.Viewport,current.Viewport!);
            }
        }
    }
    static StructureChange Set(int index,Resolved value,Database db,Transaction tr,WriteJob job)
    {
        job.CheckCancellation();
        var viewport=(Viewport)tr.GetObject(value.Before.Viewport,OpenMode.ForWrite);
        Need(viewport.IsWriteEnabled&&DatabaseMatches(viewport,db)&&viewport.OwnerId==value.Before.Block,"Native clipping target is not the proved writable viewport.");
        // Disable before clearing/replacing. The resident native operation sets
        // the association and may adjust its frame to the boundary's extents.
        if(value.ExpectedBoundary!=(value.Before.Clip?.Id??ObjectId.Null))
        {job.CheckCancellation();viewport.NonRectClipOn=false;job.CheckCancellation();viewport.NonRectClipEntityId=value.ExpectedBoundary;}
        job.CheckCancellation();viewport.NonRectClipOn=value.ExpectedEnabled;
        job.CheckCancellation();var frame=Frame(viewport);RequireFrame(value,frame);var camera=Camera(value.Before.View,frame);
        // Native clipping can also recenter the camera's WCS target. Restore
        // the requested viewing basis before compensating its DCS center;
        // retaining only ViewCenter would leave an unrequested translation.
        var target=value.Before.View.view_target;
        job.CheckCancellation();var nativeTarget=viewport.ViewTarget;
        if(nativeTarget.X!=target[0]||nativeTarget.Y!=target[1]||nativeTarget.Z!=target[2])
        {job.CheckCancellation();viewport.ViewTarget=new Point3d(target[0],target[1],target[2]);}
        // Keep the same projected Model point at each Paper coordinate. Native
        // clipping must not rescale or pan the engineering detail just because
        // its bounding frame changed. Orientation/target/twist stay untouched.
        if(!Near(viewport.ViewHeight,camera.Height)){job.CheckCancellation();viewport.ViewHeight=camera.Height;}
        job.CheckCancellation();var center=viewport.ViewCenter;
        if(!Near(center.X,camera.Center.X)||!Near(center.Y,camera.Center.Y))
        {job.CheckCancellation();viewport.ViewCenter=camera.Center;}
        return new(index,value.Edit.Operation,value.Before.Context,"viewport",value.Before.Name+":"+value.Before.Context.handle,false);
    }
    internal static StructureChange[] ApplyGroup(IReadOnlyDictionary<int,Resolved> values,Database db,Transaction tr,WriteJob job)
    {
        // Prove every input against the same pre-write sheet before any setter.
        // Complete desired-state checks admit only the explicitly planned
        // clipping/frame/map fields of other selected viewports in this group.
        foreach(var value in values.Values){job.CheckCancellation();Verify(value,db,tr,job.CheckCancellation);}
        var changes=new List<StructureChange>();
        foreach(var pair in values){job.CheckCancellation();changes.Add(Set(pair.Key,pair.Value,db,tr,job));}
        var group=values.Values.ToDictionary(v=>v.Before.Viewport);
        foreach(var value in values.Values){job.CheckCancellation();VerifyDesired(value,group,db,tr,job.CheckCancellation);}
        return changes.ToArray();
    }
    internal static void VerifyCommitted(IReadOnlyDictionary<int,Resolved> values,Database db,Action cancel)
    {
        using var tr=db.TransactionManager.StartTransaction();
        var group=values.Values.ToDictionary(v=>v.Before.Viewport);
        foreach(var value in values.Values){cancel();VerifyDesired(value,group,db,tr,cancel);}
    }
}
