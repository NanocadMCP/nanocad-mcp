using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Direct setters on existing definition members. No explode, replacement,
// instance transform, attribute synchronization or evaluated-extents oracle.
internal static class CadBlockDefinitionGeometry388
{
    private const string Warning="SHARED_BLOCK_GEOMETRY: Existing definition members and all actual referring insertions retain identity; native graphics are marked modified. Placement, literal attribute definitions/values and observed source metadata are retained. Unselected opaque owned payloads and evaluated graphics are not exhaustively observed. Refresh inspect_block and inspect the useful shared-symbol view; Undo/rollback is not guaranteed.";
    private const string Zero="00000000-0000-0000-0000-000000000000";
    internal sealed record State(ObjectId Definition,string Name,ObjectId[] References,JsonElement Value);
    internal static bool Handles(StructureWriteArguments args)=>args.Operations is [SetBlockDefinitionGeometry388];
    private static void Need([DoesNotReturnIf(false)] bool value,string message,string code="UNSUPPORTED_BLOCK_GEOMETRY")
    {if(!value)throw new DomainException(code,message);}
    private static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static bool Local(DBObject item,Database db)=>db.UnmanagedObject!=IntPtr.Zero&&item.Database?.UnmanagedObject==db.UnmanagedObject;
    private static bool Same(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    private static double N(double value)=>double.IsFinite(value)?value:throw new DomainException("INVALID_GEOMETRY","A native block coordinate is nonfinite.");
    private static double[] P(Point3d value)=>[N(value.X),N(value.Y),N(value.Z)];
    private static double[] V(Vector3d value)=>[N(value.X),N(value.Y),N(value.Z)];
    private static double[] M(Matrix3d value)
    {var result=new double[16];for(int row=0;row<4;row++)for(int column=0;column<4;column++)result[row*4+column]=N(value[row,column]);return result;}
    internal static BlockTableRecord Definition(ObjectId id,Database db,Transaction tr,bool requireUnits=true)
    {
        Need(Live(id)&&tr.GetObject(id,OpenMode.ForRead) is BlockTableRecord,"Select a current native block definition.","OBJECT_NOT_FOUND");
        var block=(BlockTableRecord)tr.GetObject(id,OpenMode.ForRead);
        Need(block.GetType()==typeof(BlockTableRecord)&&block.ObjectId==id&&block.OwnerId==db.BlockTableId&&Local(block,db),"The definition does not have the current native BlockTable owner/database.");
        Need(!block.IsAnonymous&&!block.IsDynamicBlock&&!block.IsLayout&&!block.IsDependent&&!block.IsFromExternalReference&&!block.IsFromOverlayReference&&BlockWriteValidation.OrdinaryName(block.Name),
            "Geometry editing supports ordinary local static definitions; dynamic, anonymous, layout and external definitions need their owning route.","UNSUPPORTED_BLOCK_TYPE");
        var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        Need(table.Has(block.Name)&&table[block.Name]==id,"The actual definition name index differs from its native identity.");
        if(requireUnits)Need((int)db.Insunits==4&&((int)block.Units==0||(int)block.Units==4),"Use a Millimeters drawing and Unitless/Millimeters definition; no insertion-unit conversion is performed.","UNSUPPORTED_UNITS");
        return block;
    }
    internal static ObjectId[] Members(BlockTableRecord block,Database db,Transaction tr,Action cancel)
    {
        var result=new List<ObjectId>();var seen=new HashSet<ObjectId>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId id in block)
        {
            cancel();if(id.IsErased||id.IsEffectivelyErased)continue;
            Need(Live(id)&&seen.Add(id)&&tr.GetObject(id,OpenMode.ForRead) is Entity,"A definition has an unavailable/repeated/non-entity member.");
            var entity=(Entity)tr.GetObject(id,OpenMode.ForRead);Need(entity.ObjectId==id&&entity.OwnerId==block.ObjectId&&Local(entity,db),"A member has a foreign definition owner/database.");
            budget.Add(Id(id));result.Add(id);
        }
        return result.ToArray();
    }
    private static JsonElement Header(DBObject item,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(Live(item.ObjectId)&&Live(item.OwnerId)&&Local(item,db),"A retained native object has no current local identity/owner.");
        var budget=new ModelInventoryBudget(cancel);
        object Root(DBObject value)
        {
            using var links=value.GetPersistentReactorIds();using var xdata=value.XData;var reactors=new List<string?>();
            foreach(ObjectId id in links){cancel();reactors.Add(Id(id));budget.Add(Id(id));}
            return new{handle=Id(value.ObjectId),owner=Id(value.OwnerId),type=value.GetType().FullName,value.HasFields,extension=Id(value.ExtensionDictionary),
                reactors=reactors.OrderBy(s=>s,StringComparer.Ordinal).ToArray(),xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,budget.Add)};
        }
        // Observe actual owned identities/edges and XData without treating
        // unknown private evaluated payloads as immutable user geometry.
        var owned=new List<object>();var pending=new Stack<(ObjectId Id,ObjectId Owner)>();var seen=new HashSet<ObjectId>();
        if(!item.ExtensionDictionary.IsNull)pending.Push((item.ExtensionDictionary,item.ObjectId));
        while(pending.Count>0)
        {
            cancel();var next=pending.Pop();Need(Live(next.Id)&&seen.Add(next.Id),"An owned metadata identity is unavailable/repeated.");
            var value=tr.GetObject(next.Id,OpenMode.ForRead);Need(value.ObjectId==next.Id&&value.OwnerId==next.Owner&&Local(value,db),"An owned metadata node has a foreign native owner/database.");
            var entries=new List<object>();
            if(value is DBDictionary dictionary)
                foreach(DBDictionaryEntry entry in dictionary)
                {
                    cancel();Need(Live(entry.Value),"An owned dictionary entry is unavailable.");var child=tr.GetObject(entry.Value,OpenMode.ForRead);
                    Need(Local(child,db)&&child.ObjectId==entry.Value,"A retained dictionary entry belongs to another database.");
                    bool owns=child.OwnerId==value.ObjectId;var row=new{name=entry.Key,id=Id(entry.Value),owner=Id(child.OwnerId),type=child.GetType().FullName,owned=owns};budget.Add(row);entries.Add(row);
                    if(owns)pending.Push((entry.Value,value.ObjectId));
                }
            var record=new{header=Root(value),entries=entries.OrderBy(e=>Wire.Element(e).GetProperty("name").GetString(),StringComparer.Ordinal).ToArray()};budget.Add(record);owned.Add(record);
            if(!value.ExtensionDictionary.IsNull)pending.Push((value.ExtensionDictionary,value.ObjectId));
        }
        var result=Wire.Element(new{root=Root(item),owned_headers=owned.OrderBy(e=>Wire.Element(e).GetProperty("header").GetProperty("handle").GetString(),StringComparer.Ordinal).ToArray()});budget.Add(result);return result;
    }
    private static object Geometry(Entity value)=>value switch
    {
        Line line when line.GetType()==typeof(Line)=>new{type="Line",start=P(line.StartPoint),end=P(line.EndPoint),normal=V(line.Normal),thickness=N(line.Thickness)},
        Circle circle when circle.GetType()==typeof(Circle)=>new{type="Circle",center=P(circle.Center),radius=N(circle.Radius),normal=V(circle.Normal),thickness=N(circle.Thickness)},
        _=>throw new DomainException("UNSUPPORTED_BLOCK_MEMBER","Only exact existing Line/Circle members have a direct geometry edit route.")
    };
    private static bool Editable(Entity value)=>value.GetType()==typeof(Line)||value.GetType()==typeof(Circle)||value.GetType()==typeof(Arc)||value.GetType()==typeof(Polyline)||value.GetType()==typeof(Ellipse)||value.GetType()==typeof(Spline);
    private static void ReplacementSource(Entity value,Action cancel)
    {
        // NurbsData replacement uses the established clamped source adapter.
        // Keep wider fit/periodic observations in unrelated retained members.
        if(value is Spline spline)_=CadSplineReader.Read(spline,cancel);
    }
    private static object EditingGeometry(Entity value,Action cancel)
    {
        if(value is Line line)return new{type="Line",start=P(line.StartPoint),end=P(line.EndPoint)};
        if(value is Circle circle)return new{type="Circle",center=P(circle.Center),radius=N(circle.Radius)};
        if(value is Arc arc)return new{type="Arc",center=P(arc.Center),radius=N(arc.Radius),start_angle=ContourGeometry.Angle(arc.StartAngle),sweep_angle=ContourGeometry.Angle(arc.EndAngle-arc.StartAngle)};
        if(value is Ellipse ellipse)return new{type="Ellipse",center=P(ellipse.Center),major_axis=V(ellipse.MajorAxis),radius_ratio=N(ellipse.RadiusRatio),start_parameter=ContourGeometry.Angle(ellipse.StartParam),sweep_parameter=ellipse.Closed?2*Math.PI:ContourGeometry.Angle(ellipse.EndParam-ellipse.StartParam)};
        if(value is Polyline polyline)
        {
            var vertices=new List<object>();var budget=new ModelInventoryBudget(cancel);
            for(int i=0;i<polyline.NumberOfVertices;i++){cancel();var p=polyline.GetPoint2dAt(i);var vertex=new{position=new[]{N(p.X),N(p.Y),0d},bulge=N(polyline.GetBulgeAt(i)),start_width=N(polyline.GetStartWidthAt(i)),end_width=N(polyline.GetEndWidthAt(i))};budget.Add(vertex);vertices.Add(vertex);}
            return new{type="Polyline",closed=polyline.Closed,vertices=vertices.ToArray()};
        }
        if(value is Spline spline)
        {
            var data=CadNativeCurveInspection342.ReadSpline(spline,cancel);double span=data.end_parameter-data.start_parameter;
            Need(span>0&&double.IsFinite(span),"Native spline has no finite parameter span.");
            return new{type="Spline",degree=data.degree,closed=data.closed,control_points=data.control_points,knots=data.knots.Select(k=>(k-data.start_parameter)/span).ToArray(),weights=data.rational?data.weights:[]};
        }
        throw new DomainException("UNSUPPORTED_BLOCK_MEMBER","Select a direct Line, Circle, Arc, Polyline, Ellipse or Spline member.");
    }
    private static void Ordinary2d(Entity value,Action cancel)
    {
        cancel();bool XY(Point3d p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)&&Math.Abs(p.Z)<=1e-9;
        bool Z(Vector3d n)=>Math.Abs(n.X)<=1e-9&&Math.Abs(n.Y)<=1e-9&&Math.Abs(n.Z-1)<=1e-9;
        bool planar=value switch
        {
            Line line when value.GetType()==typeof(Line)=>XY(line.StartPoint)&&XY(line.EndPoint)&&Z(line.Normal)&&Math.Abs(line.Thickness)<=1e-9,
            Circle circle when value.GetType()==typeof(Circle)=>XY(circle.Center)&&Z(circle.Normal)&&Math.Abs(circle.Thickness)<=1e-9,
            Arc arc when value.GetType()==typeof(Arc)=>XY(arc.Center)&&Z(arc.Normal)&&Math.Abs(arc.Thickness)<=1e-9,
            Ellipse ellipse when value.GetType()==typeof(Ellipse)=>XY(ellipse.Center)&&Z(ellipse.Normal),
            Polyline polyline when value.GetType()==typeof(Polyline)=>Z(polyline.Normal)&&Math.Abs(polyline.Elevation)<=1e-9&&Math.Abs(polyline.Thickness)<=1e-9,
            DBText text when value.GetType()==typeof(DBText)||value.GetType()==typeof(AttributeDefinition)=>XY(text.Position)&&Z(text.Normal),
            MText text when value.GetType()==typeof(MText)=>XY(text.Location)&&Z(text.Normal),
            Hatch hatch when value.GetType()==typeof(Hatch)=>Z(hatch.Normal)&&Math.Abs(hatch.Elevation)<=1e-9&&!hatch.Associative&&hatch.HatchObjectType==HatchObjectType.HatchObject,
            Spline spline when value.GetType()==typeof(Spline)=>SplineXY(spline,cancel),
            BlockReference reference when value.GetType()==typeof(BlockReference)||value.GetType()==typeof(MInsertBlock)=>!reference.IsDynamicBlock,
            _=>false
        };
        Need(planar,"Related definition member "+Id(value.ObjectId)+" ("+value.GetType().Name+") is outside the ordinary local XY2D source scope; native model/3D/custom members require their owning route.");
        Need(!value.HasFields,"Related definition member "+Id(value.ObjectId)+" is Field-bearing; use its owning data route.");
    }
    private static bool SplineXY(Spline value,Action cancel)
    {
        var data=CadNativeCurveInspection342.ReadSpline(value,cancel);
        foreach(var point in data.control_points){cancel();if(Math.Abs(point[2])>1e-9)return false;}
        if(data.native_inspection.fit_data is {} fit)foreach(var point in fit.points){cancel();if(Math.Abs(point[2])>1e-9)return false;}
        return data.control_points.Length>0;
    }
    internal static object StoredGeometry(Entity value,Action cancel)
    {
        if(value.GetType()==typeof(Line)||value.GetType()==typeof(Circle))return Geometry(value);
        if(value is AttributeDefinition attribute)return Attribute(attribute,cancel);
        if(value.GetType()==typeof(Hatch))return CadBlockTemplate391.StoredHatch((Hatch)value,cancel);
        if(value is Arc arc)return new{type="Arc",center=P(arc.Center),radius=N(arc.Radius),normal=V(arc.Normal),start_angle=N(arc.StartAngle),end_angle=N(arc.EndAngle),thickness=N(arc.Thickness)};
        if(value is Ellipse ellipse)return new{type="Ellipse",center=P(ellipse.Center),major_axis=V(ellipse.MajorAxis),normal=V(ellipse.Normal),radius_ratio=N(ellipse.RadiusRatio),start_parameter=N(ellipse.StartParam),end_parameter=N(ellipse.EndParam)};
        if(value is Polyline polyline)
        {
            var vertices=new List<object>();var budget=new ModelInventoryBudget(cancel);
            for(int i=0;i<polyline.NumberOfVertices;i++)
            {cancel();var point=polyline.GetPoint2dAt(i);var row=new{point=new[]{N(point.X),N(point.Y)},bulge=N(polyline.GetBulgeAt(i)),start_width=N(polyline.GetStartWidthAt(i)),end_width=N(polyline.GetEndWidthAt(i))};budget.Add(row);vertices.Add(row);}
            return new{type="Polyline",polyline.Closed,elevation=N(polyline.Elevation),normal=V(polyline.Normal),thickness=N(polyline.Thickness),constant_width=N(polyline.ConstantWidth),vertices=vertices.ToArray()};
        }
        if(value is Spline spline)
        {
            var data=CadNativeCurveInspection342.ReadSpline(spline,cancel);
            return new{type="Spline",data.degree,data.rational,data.periodic,data.closed,data.has_fit_data,data.control_points,data.knots,data.weights,
                data.control_point_tolerance,data.knot_tolerance,fit_data=data.native_inspection.fit_data,fit_data_unavailable_reason=data.native_inspection.fit_data_unavailable_reason};
        }
        if(value is DBText text)return new{type="Text",text.TextString,position=P(text.Position),alignment_point=P(text.AlignmentPoint),normal=V(text.Normal),text.Height,text.Rotation,
            text.WidthFactor,text.Oblique,text.Thickness,text.IsMirroredInX,text.IsMirroredInY,horizontal=text.HorizontalMode.ToString(),vertical=text.VerticalMode.ToString(),style=Id(text.TextStyleId)};
        if(value is MText mtext)
        {
            using var background=mtext.BackgroundFillColor;var heights=new List<double>();var budget=new ModelInventoryBudget(cancel);bool columns=mtext.ColumnType==ColumnType.StaticColumns;
            if(columns)for(int i=0;i<mtext.ColumnCount;i++){cancel();double h=N(mtext.GetColumnHeight(i));budget.Add(h);heights.Add(h);}
            return new{type="MText",mtext.Contents,location=P(mtext.Location),normal=V(mtext.Normal),direction=V(mtext.Direction),mtext.Width,mtext.TextHeight,
                attachment=mtext.Attachment.ToString(),style=Id(mtext.TextStyleId),mtext.LineSpacingFactor,line_spacing=mtext.LineSpacingStyle.ToString(),flow=mtext.FlowDirection.ToString(),
                mtext.BackgroundFill,mtext.UseBackgroundColor,mtext.BackgroundScaleFactor,background_color=new{method=background.ColorMethod.ToString(),aci=background.IsByAci?(int?)background.ColorIndex:null,rgb=background.IsByColor?new[]{(int)background.Red,(int)background.Green,(int)background.Blue}:null},
                transparency=new{alpha=mtext.BackgroundTransparency.Alpha,mtext.BackgroundTransparency.IsByAlpha,mtext.BackgroundTransparency.IsByLayer,mtext.BackgroundTransparency.IsByBlock},
                column_type=mtext.ColumnType.ToString(),mtext.ColumnWidth,mtext.ColumnGutterWidth,mtext.ColumnAutoHeight,mtext.ColumnFlowReversed,
                explicit_column_count=columns?(int?)mtext.ColumnCount:null,explicit_column_heights=heights.ToArray(),dynamic_fixed_height=mtext.ColumnType==ColumnType.DynamicColumns&&!mtext.ColumnAutoHeight?(double?)mtext.Height:null};
        }
        throw new DomainException("UNSUPPORTED_BLOCK_MEMBER","An unclassified related definition member has no stored 2D geometry adapter.");
    }
    private static object Attribute(DBText value,Action cancel)
    {
        Need(!value.HasFields,"A Field-bearing attribute needs its owning data edit route; its rendered text is not a stored-value oracle.");
        object? multiline=NativeAttributeMText377.Read(value,id=>new EntityRef(Zero,Zero,Id(id)!),cancel);
        Need(multiline==null||!Wire.Element(multiline).GetProperty("has_fields").GetBoolean(),"An embedded Field-bearing MText attribute needs its owning data route.");
        bool embedded=multiline!=null;
        return new{tag=value is AttributeDefinition definition?definition.Tag:((AttributeReference)value).Tag,
            dbtext=embedded?null:new{text=value.TextString,position=P(value.Position),height=N(value.Height),rotation=N(value.Rotation),normal=V(value.Normal),
                style=Id(value.TextStyleId),value.WidthFactor,value.Oblique,horizontal=value.HorizontalMode.ToString(),vertical=value.VerticalMode.ToString(),alignment_point=P(value.AlignmentPoint)},multiline,
            definition=value is AttributeDefinition d?new{d.Prompt,d.Preset,d.Verifiable,d.LockPositionInBlock,d.Invisible,d.Constant,d.IsMTextAttributeDefinition}:null,
            occurrence=value is AttributeReference a?new{a.LockPositionInBlock,a.Invisible,a.IsConstant,a.IsMirroredInX,a.IsMirroredInY,a.IsMTextAttribute}:null};
    }
    private static JsonElement Member(Entity value,Database db,Transaction tr,Action cancel,bool selected,bool textObserved=false)
    {
        Ordinary2d(value,cancel);object geometry=StoredGeometry(value,cancel);
        JsonElement? owned=null;string? ownedReason=null;
        if(Editable(value)||textObserved&&(value.GetType()==typeof(DBText)||value.GetType()==typeof(MText)))
            try{owned=NativeOwnedMetadata.Capture(value,tr,cancel);}
            catch(OperationCanceledException){throw;}catch(DomainException error){if(selected)throw;ownedReason=error.Error.code;}
        return Wire.Element(new{header=Header(value,db,tr,cancel),properties=EntityAdapter.StoredProperties(value),layer=Id(value.LayerId),annotative=value.Annotative.ToString(),
            geometry,curve_owned=owned,curve_owned_unavailable_reason=ownedReason});
    }
    internal static void Writable(Entity entity,Database db,Transaction tr)
    {
        Need(Live(entity.LayerId)&&tr.GetObject(entity.LayerId,OpenMode.ForRead) is LayerTableRecord,"An affected member/insertion layer is unavailable.");
        var layer=(LayerTableRecord)tr.GetObject(entity.LayerId,OpenMode.ForRead);
        Need(Local(layer,db)&&layer.OwnerId==db.LayerTableId&&!layer.IsDependent&&!layer.IsLocked,"An affected member/insertion layer is dependent or locked.","LAYER_NOT_WRITABLE");
    }
    private static JsonElement Reference(BlockReference value,Database db,Transaction tr,Action cancel,bool writable,bool requireOrdinary=true)
    {
        Need((value.GetType()==typeof(BlockReference)||value.GetType()==typeof(MInsertBlock))&&Local(value,db),"An insertion is custom or foreign; use its owning route.");
        if(requireOrdinary)Need(!value.IsDynamicBlock&&value.Annotative!=AnnotativeStates.True,"An affected insertion is dynamic/annotative; use its owning route.");
        Need(!value.HasFields,"A Field-bearing affected insertion needs its owning data route.");
        if(writable)Writable(value,db,tr);
        Need(Live(value.OwnerId)&&tr.GetObject(value.OwnerId,OpenMode.ForRead) is BlockTableRecord,"An insertion has no resident block-record owner.");
        var owner=(BlockTableRecord)tr.GetObject(value.OwnerId,OpenMode.ForRead);
        bool contains=false;foreach(ObjectId id in owner){cancel();if(id==value.ObjectId){contains=true;break;}}
        Need(Local(owner,db)&&contains,"An insertion is absent from its actual native owner membership.");
        if(owner.IsLayout)_=CadBlockSpace.RequireSpace(db,tr,owner.ObjectId);else _=Definition(owner.ObjectId,db,tr,false);
        var attributes=new List<object>();var used=new HashSet<ObjectId>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId id in value.AttributeCollection)
        {
            cancel();Need(Live(id)&&used.Add(id)&&tr.GetObject(id,OpenMode.ForRead) is AttributeReference,"An affected insertion owns an unavailable/repeated/non-attribute object.");
            var attr=(AttributeReference)tr.GetObject(id,OpenMode.ForRead);Need(attr.GetType()==typeof(AttributeReference)&&attr.ObjectId==id&&attr.OwnerId==value.ObjectId&&Local(attr,db),"An occurrence attribute has a foreign native owner/type.");
            var row=new{header=Header(attr,db,tr,cancel),properties=EntityAdapter.StoredProperties(attr),layer=Id(attr.LayerId),stored=Attribute(attr,cancel)};budget.Add(row);attributes.Add(row);
        }
        var scale=value.ScaleFactors;
        var result=Wire.Element(new{header=Header(value,db,tr,cancel),definition=Id(value.BlockTableRecord),position=P(value.Position),normal=V(value.Normal),rotation=N(value.Rotation),
            scale=new[]{N(scale.X),N(scale.Y),N(scale.Z)},transform=M(value.BlockTransform),properties=EntityAdapter.StoredProperties(value),layer=Id(value.LayerId),
            value.IsDynamicBlock,annotative=value.Annotative.ToString(),array=value is MInsertBlock array?CadBlockArray384.Parameters(array):null,attributes=attributes.ToArray()});budget.Add(result);return result;
    }
    internal static State Capture(ObjectId target,HashSet<ObjectId> selected,Database db,Transaction tr,Action cancel,bool writable,bool textSelected=false,
        bool membershipSelected=false,bool requireMembershipUnits=true,ObjectId[]? retainedDefinitions=null)
    {
        bool units=!textSelected&&(!membershipSelected||requireMembershipUnits);
        var root=Definition(target,db,tr,units);var members=Members(root,db,tr,cancel);var contents=new List<JsonElement>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId id in members)
        {
            cancel();var member=(Entity)tr.GetObject(id,OpenMode.ForRead);Ordinary2d(member,cancel);
            if(selected.Contains(id))
            {
                if(membershipSelected)CadBlockDefinitionMembers392.RequireRetirement(member,db,tr,cancel);
                else if(textSelected)CadBlockDefinitionText391.RequireLiteral(member,cancel);
                else Need(Editable(member),"Select direct native Line/Circle/Arc/Polyline/Ellipse/Spline members of this definition.");
                Need(member.Annotative!=AnnotativeStates.True,"An annotative member needs its owning edit route.");
                if(!textSelected&&!membershipSelected){CadIndependentCurve.Require(member,db,tr,cancel);ReplacementSource(member,cancel);}if(writable)Writable(member,db,tr);
            }
            // Nested insertion pose/attributes are retained as native sources.
            var row=member is BlockReference reference?Reference(reference,db,tr,cancel,false,false):Member(member,db,tr,cancel,!membershipSelected&&selected.Contains(id),textSelected);budget.Add(row);contents.Add(row);
        }
        Need(selected.IsSubsetOf(members.ToHashSet()),"A requested handle is not an actual direct member of the selected definition.");
        // Discover all direct and transitive insertions from actual resident
        // BTR membership; no cached reference count or hidden model-count cap.
        var links=new List<(ObjectId Ref,ObjectId Owner,ObjectId Definition)>();var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        foreach(ObjectId ownerId in table)
        {
            cancel();if(ownerId.IsErased||ownerId.IsEffectivelyErased)continue;Need(Live(ownerId),"A block-record identity is unavailable.");
            var owner=(BlockTableRecord)tr.GetObject(ownerId,OpenMode.ForRead);
            foreach(ObjectId id in owner)
            {
                cancel();if(id.IsErased||id.IsEffectivelyErased)continue;
                if(tr.GetObject(id,OpenMode.ForRead) is not BlockReference reference)continue;
                links.Add((id,ownerId,reference.BlockTableRecord));
            }
        }
        var definitions=new HashSet<ObjectId>{target};var references=new HashSet<ObjectId>();bool added;
        do
        {
            added=false;foreach(var link in links)
            {
                cancel();if(!definitions.Contains(link.Definition))continue;
                var reference=(BlockReference)tr.GetObject(link.Ref,OpenMode.ForRead);
                Need(reference.ObjectId==link.Ref&&reference.OwnerId==link.Owner&&Local(reference,db),"An affected insertion has inconsistent native owner/database membership.");references.Add(link.Ref);
                var owner=(BlockTableRecord)tr.GetObject(link.Owner,OpenMode.ForRead);if(!owner.IsLayout&&definitions.Add(link.Owner))added=true;
            }
        }while(added);
        var referenceRows=new List<JsonElement>();foreach(var id in references.OrderBy(Id,StringComparer.Ordinal))
        {cancel();var row=Reference((BlockReference)tr.GetObject(id,OpenMode.ForRead),db,tr,cancel,writable);budget.Add(row);referenceRows.Add(row);}
        // Every related definition, including descendants and ancestor
        // siblings, must be an actual ordinary 2D source graph. ModelSpace
        // consumer scans alone do not prove nested native-model independence.
        // A composition edit can orphan a child definition by retiring its
        // last local reference. Observe those retained sources without
        // seeding the actual target-consumer discovery with extra definitions.
        var inspected=new HashSet<ObjectId>();var pending=new Stack<ObjectId>(definitions.Concat(retainedDefinitions??[]));var definitionRows=new List<object>();
        while(pending.Count>0)
        {
            cancel();var id=pending.Pop();if(!inspected.Add(id))continue;var parent=Definition(id,db,tr,units&&id==target);var own=Members(parent,db,tr,cancel);var preserved=new List<JsonElement>();
            foreach(var memberId in own)
            {
                cancel();var member=(Entity)tr.GetObject(memberId,OpenMode.ForRead);Ordinary2d(member,cancel);
                if(member is BlockReference reference)
                {Need(!reference.IsDynamicBlock,"A related nested insertion is dynamic; use its owning route.");pending.Push(reference.BlockTableRecord);if(id!=target)preserved.Add(Reference(reference,db,tr,cancel,false));}
                else if(id!=target)preserved.Add(Member(member,db,tr,cancel,false));
            }
            var row=new{header=Header(parent,db,tr,cancel),parent.Name,origin=P(parent.Origin),units=(int)parent.Units,scaling=(int)parent.BlockScaling,
                parent.Explodable,members=own.Select(Id).ToArray(),unselected_contents=preserved.ToArray()};budget.Add(row);definitionRows.Add(row);
        }
        var result=Wire.Element(new{definition=Id(target),definitions=definitionRows.OrderBy(row=>Wire.Element(row).GetProperty("header").GetProperty("root").GetProperty("handle").GetString(),StringComparer.Ordinal).ToArray(),contents=contents.ToArray(),references=referenceRows.ToArray()});budget.Add(result);
        return new(target,root.Name,references.OrderBy(Id,StringComparer.Ordinal).ToArray(),result);
    }
    internal static string Token(State state)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state.Value,Wire.Json)));
    private static JsonElement Invariants(State state,SetBlockDefinitionGeometry388 edit)
    {
        var node=JsonNode.Parse(state.Value.GetRawText())!.AsObject();var ids=edit.Members.Select(e=>e.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var row in node["contents"]!.AsArray())
            if(row?["header"]?["root"]?["handle"] is JsonValue handle&&ids.Contains(handle.GetValue<string>()))
            {
                var geometry=row!["geometry"]!.AsObject();
                // Preserve stored plane/thickness and native type. The explicitly
                // replaced geometric definition may change topology/parameterization.
                foreach(string key in geometry.Select(p=>p.Key).ToArray())
                    if(key!="type"&&key!="normal"&&key!="thickness"&&key!="elevation")geometry.Remove(key);
            }
        return Wire.Element(node);
    }
    private static State Resolve(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel)
    {
        Need(Handles(args),"Edit one shared definition in a separate request.","INVALID_ARGUMENT");var edit=(SetBlockDefinitionGeometry388)args.Operations[0];var db=state.Document.Database;
        var target=CadStructureWriter.Find(db,edit.DefinitionHandle);var selected=edit.Members.Select(e=>CadStructureWriter.Find(db,e.Handle)).ToHashSet();
        var before=Capture(target,selected,db,tr,cancel,true);
        Need(Token(before)==edit.ExpectedDefinitionToken,"Definition, members or referring insertions changed; refresh inspect_block.","STALE_BLOCK_DEFINITION_TOKEN");
        foreach(var member in edit.Members)
        {var entity=(Entity)tr.GetObject(CadStructureWriter.Find(db,member.Handle),OpenMode.ForRead);Need(entity.GetType().Name==member.Type,"Requested geometry type differs from the actual native member.","UNSUPPORTED_BLOCK_MEMBER");}
        return before;
    }
    internal static void Preflight(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel)=>_=Resolve(args,state,tr,cancel);
    internal static object TryRead(ObjectId target,string bridge,string document,Database db,Transaction tr,Action cancel)
    {
        var rows=new List<object>();var refs=Array.Empty<EntityRef>();string? token=null;string? unavailable=null;string name="";
        EntityRef Ref(ObjectId id)=>new(bridge,document,Id(id)!);
        try
        {
            if(Live(target)&&tr.GetObject(target,OpenMode.ForRead) is BlockTableRecord actual)name=actual.Name;
            var block=Definition(target,db,tr);name=block.Name;var selected=new HashSet<ObjectId>();
            foreach(var id in Members(block,db,tr,cancel))
            {
                cancel();var entity=(Entity)tr.GetObject(id,OpenMode.ForRead);if(!Editable(entity))continue;
                string? reason=null;try{Need(entity.Annotative!=AnnotativeStates.True,"Annotative native members need their owning route.");CadIndependentCurve.Require(entity,db,tr,cancel);ReplacementSource(entity,cancel);Writable(entity,db,tr);_=NativeOwnedMetadata.Capture(entity,tr,cancel);selected.Add(id);}
                catch(OperationCanceledException){throw;}catch(System.Exception error){reason=Reason(error);}
                object geometry=EditingGeometry(entity,cancel);
                rows.Add(new{@ref=Ref(id),geometry,eligible=reason==null,unavailable_reason=reason});
            }
            var capture=Capture(target,selected,db,tr,cancel,true);refs=capture.References.Select(Ref).ToArray();token=Token(capture);
            if(selected.Count==0)unavailable="This definition has no currently eligible direct native Line/Circle/Arc/Polyline/Ellipse/Spline member.";
        }
        catch(OperationCanceledException){throw;}catch(System.Exception error){unavailable=Reason(error);}
        var result=new{definition_ref=Ref(target),definition_name=name,coordinate_system="parent_definition",definition_token=token,can_edit=unavailable==null,
            unavailable_reason=unavailable,members=rows.ToArray(),referencing_insertions=refs};SerializedCapacity357.Check(result,Wire.PayloadBudget-8192,cancel);return result;
    }
    private static string Reason(System.Exception error)
    {string value=(error is DomainException domain?domain.Error.code:error.GetType().Name)+": "+error.Message;return value.Length<=1024?value:value[..1024];}
    private static void Requested(SetBlockDefinitionGeometry388 edit,Database db,Transaction tr,Action cancel)
    {
        bool Close(double a,double b)=>Math.Abs(a-b)<=1e-7;
        bool Point(Point3d a,Point3 b)=>Close(a.X,b.X)&&Close(a.Y,b.Y)&&Close(a.Z,b.Z);
        foreach(var member in edit.Members)
        {
            cancel();
            var entity=(Entity)tr.GetObject(CadStructureWriter.Find(db,member.Handle),OpenMode.ForRead);
            bool same=member.Type=="Line"&&entity is Line line&&Point(line.StartPoint,member.Start!)&&Point(line.EndPoint,member.End!)||
                member.Type=="Circle"&&entity is Circle circle&&Point(circle.Center,member.Center!)&&Close(circle.Radius,member.Radius!.Value);
            if(member.Type=="Arc"&&entity is Arc arc)
                same=Point(arc.Center,member.Center!)&&Close(arc.Radius,member.Radius!.Value)&&Close(ContourGeometry.Angle(arc.EndAngle-arc.StartAngle),member.SweepAngle!.Value)&&
                    Math.Abs(Math.IEEERemainder(arc.StartAngle-member.StartAngle!.Value,2*Math.PI))<=1e-7;
            else if(member.Type=="Ellipse"&&entity is Ellipse ellipse){CadEllipseCreator.Verify(ellipse,member.Ellipse!,cancel);same=true;}
            else if(member.Type=="Spline"&&entity is Spline spline){CadSplineCreator.Verify(spline,member.Spline!,cancel);same=true;}
            else if(member.Type=="Polyline"&&entity is Polyline polyline)
            {
                var vertices=new List<PolylineShapeVertex>();for(int i=0;i<polyline.NumberOfVertices;i++){cancel();var p=polyline.GetPoint2dAt(i);vertices.Add(new(new(p.X,p.Y),polyline.GetBulgeAt(i),polyline.GetStartWidthAt(i),polyline.GetEndWidthAt(i)));}
                same=PolylineTopologyGeometry.Equivalent(new(polyline.Closed,vertices.ToArray()),member.Polyline!,cancel);
            }
            if(!same)throw new DomainException("NATIVE_BLOCK_GEOMETRY_MISMATCH","An existing native definition member did not retain its requested geometry.",false,
                new{member_handle=member.Handle,requested=member,actual=EditingGeometry(entity,cancel)});
        }
    }
    private static void SetGeometry(Entity entity,BlockMemberGeometry388 member,Action cancel)
    {
        Point3d Point(Point3 p)=>new(p.X,p.Y,p.Z);
        if(entity is Line line){line.StartPoint=Point(member.Start!);line.EndPoint=Point(member.End!);}
        else if(entity is Circle circle){circle.Center=Point(member.Center!);circle.Radius=member.Radius!.Value;}
        else if(entity is Arc arc){arc.Center=Point(member.Center!);arc.Radius=member.Radius!.Value;arc.StartAngle=member.StartAngle!.Value;arc.EndAngle=ContourGeometry.Angle(member.StartAngle.Value+member.SweepAngle!.Value);}
        else if(entity is Ellipse ellipse)
        {
            var spec=member.Ellipse!;ellipse.Set(new(spec.Center.X,spec.Center.Y,0),Vector3d.ZAxis,new(spec.MajorAxis.X,spec.MajorAxis.Y,0),spec.RadiusRatio,0,2*Math.PI);
            if(spec.SweepParameter!=2*Math.PI){CadEllipseCreator.SetParameter(ellipse,"set_StartParam",spec.StartParameter);double end=spec.StartParameter+spec.SweepParameter;CadEllipseCreator.SetParameter(ellipse,"set_EndParam",end>2*Math.PI?end-2*Math.PI:end);}
        }
        else if(entity is Spline spline)
        {using var template=CadSplineCreator.Create(member.Spline!);var data=template.NurbsData;using var points=data.GetControlPoints();cancel();spline.NurbsData=data;}
        else if(entity is Polyline polyline)
        {
            var shape=member.Polyline!;while(polyline.NumberOfVertices>shape.Vertices.Length){cancel();polyline.RemoveVertexAt(polyline.NumberOfVertices-1);}
            while(polyline.NumberOfVertices<shape.Vertices.Length){cancel();int i=polyline.NumberOfVertices;var v=shape.Vertices[i];polyline.AddVertexAt(i,new(v.Position.X,v.Position.Y),v.Bulge,v.StartWidth,v.EndWidth);}
            for(int i=0;i<shape.Vertices.Length;i++){cancel();var v=shape.Vertices[i];polyline.SetPointAt(i,new(v.Position.X,v.Position.Y));polyline.SetBulgeAt(i,v.Bulge);polyline.SetStartWidthAt(i,v.StartWidth);polyline.SetEndWidthAt(i,v.EndWidth);}
            polyline.Closed=shape.Closed;
        }
        else throw new DomainException("UNSUPPORTED_BLOCK_MEMBER","This definition member has no direct native geometry setter.");
    }
    private static void Retained(JsonElement before,JsonElement after,SetBlockDefinitionGeometry388 edit,string stage,Action cancel)
    {
        if(Same(before,after))return;
        string Hash(JsonElement value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
        var sections=new List<object>();foreach(var field in before.EnumerateObject())
        {cancel();bool found=after.TryGetProperty(field.Name,out var value);sections.Add(new{section=field.Name,retained=found&&Same(field.Value,value),before_sha256=Hash(field.Value),after_sha256=found?Hash(value):null});}
        throw new DomainException("NATIVE_BLOCK_GEOMETRY_MISMATCH","Shared geometry changed retained native member/owner/placement/attribute/source observations.",false,
            new{definition_handle=edit.DefinitionHandle,stage,selected_member_handles=edit.Members.Select(e=>e.Handle).ToArray(),sections=sections.ToArray()});
    }
    internal static Result Execute(StructureWriteArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? transaction=null;bool started=false,commitAttempted=false;string stage="resolve";
        try
        {
            var db=state.Document.Database;var edit=(SetBlockDefinitionGeometry388)args.Operations[0];transaction=db.TransactionManager.StartTransaction();
            var before=Resolve(args,state,transaction,job.CheckCancellation);var selected=edit.Members.Select(e=>CadStructureWriter.Find(db,e.Handle)).ToHashSet();var invariant=Invariants(before,edit);
            var children=edit.Members.Select((e,i)=>new StructureChild(new(args.BridgeId,args.DocumentId,e.Handle),e.Type,i,null))
                .Concat(before.References.Select(id=>new StructureChild(new(args.BridgeId,args.DocumentId,Id(id)!),"block_reference",0,null))).ToArray();
            var data=new ModifiedStructure(args.OperationId,"committed",false,[new(0,edit.Operation,new(args.BridgeId,args.DocumentId,edit.DefinitionHandle),"block_definition",before.Name,false,children)],"not_guaranteed");
            SerializedCapacity357.Check(data,Wire.PayloadBudget-8192,job.CheckCancellation);job.CheckCancellation();stage="native_member_setters";started=true;
            log.Write(job.Operation,job.OriginalRequestId,"block_definition_geometry_started",operationId:job.OperationId);
            foreach(var member in edit.Members)
            {
                job.CheckCancellation();var entity=(Entity)transaction.GetObject(CadStructureWriter.Find(db,member.Handle),OpenMode.ForWrite);
                SetGeometry(entity,member,job.CheckCancellation);
                entity.RecordGraphicsModified(true);
            }
            stage="native_graphics_notification";foreach(var id in before.References){job.CheckCancellation();((Entity)transaction.GetObject(id,OpenMode.ForWrite)).RecordGraphicsModified(true);}
            stage="native_readback";Requested(edit,db,transaction,job.CheckCancellation);Retained(invariant,Invariants(Capture(before.Definition,selected,db,transaction,job.CheckCancellation,true),edit),edit,stage,job.CheckCancellation);
            job.CheckCancellation();stage="commit";job.BeginCommit();commitAttempted=true;transaction.Commit();transaction.Dispose();transaction=null;
            stage="postcommit_readback";using(var read=db.TransactionManager.StartTransaction())
            {Requested(edit,db,read,job.CheckCancellation);Retained(invariant,Invariants(Capture(before.Definition,selected,db,read,job.CheckCancellation,true),edit),edit,stage,job.CheckCancellation);}
            var result=Result.Ok(job.OriginalRequestId,data,context,Warning,WriteRecovery.Warning);job.RecordCommit(result);return result;
        }
        catch(System.Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;System.Exception? cleanup=null;try{transaction?.Dispose();}catch(System.Exception caught){cleanup=caught;}
            string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"BLOCK_GEOMETRY_FAILED";
            return job.Error(started?"WRITE_OUTCOME_UNKNOWN":cause,started?"Native shared block geometry started but the complete outcome is unverified; refresh the definition and its actual insertions before further dependent writes.":error.Message,
                started?"unknown":"not_started",context,new{cause,stage,commit_attempted=commitAttempted,message=Reason(error),native_details=(error as DomainException)?.Error.details,cleanup_error=cleanup?.GetType().Name})with{warnings=[Warning,WriteRecovery.Warning]};
        }
    }
}
