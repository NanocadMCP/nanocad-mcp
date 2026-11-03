using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadMLeaderGeometry388
{
    internal sealed record Geometry(JsonElement Content,MLeaderLineRead388[] Lines,JsonElement Layout,JsonElement Display);
    internal sealed record State(Geometry Geometry,JsonElement Metadata);
    private const string Warning="NATIVE_MLEADER_GEOMETRY_NON_DRIVING: Native leader vertices were edited in place; no source/dimension/sketch association was created. Read native layout and inspect callout placement. Undo/recovery is not guaranteed.";
    private static void Need([DoesNotReturnIf(false)] bool yes,string message,string code="UNSUPPORTED_MLEADER_GEOMETRY")
    {if(!yes)throw new DomainException(code,message);}
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static bool Same(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    private static double[] P(Point3d p)
    {Need(double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)&&Math.Abs(p.X)<=1e9&&Math.Abs(p.Y)<=1e9&&Math.Abs(p.Z)<=Tolerance.Global.EqualPoint,"Leader vertex is outside finite WCS XY/Z0.","INVALID_GEOMETRY");return new[]{p.X,p.Y,p.Z};}
    private static Point3d P(double[] p)=>new(p[0],p[1],p[2]);
    private static bool Near(Point3d a,Point3d b)=>a.DistanceTo(b)<=Tolerance.Global.EqualPoint;
    private static bool SamePoint(JsonElement a,JsonElement b)=>Near(P(a.EnumerateArray().Select(v=>v.GetDouble()).ToArray()),P(b.EnumerateArray().Select(v=>v.GetDouble()).ToArray()));
    private static JsonElement Display(MLeader source)=>Wire.Element(new{layer=Id(source.LayerId),linetype=Id(source.LinetypeId),raw_color_index=source.ColorIndex,properties=EntityAdapter.StoredProperties(source)});
    internal static Geometry CaptureGeometry(MLeader leader,Action cancel)
    {
        cancel();Need(leader.GetType()==typeof(MLeader)&&leader.ContentType==ContentType.MTextContent&&!leader.HasFields&&!leader.EnableAnnotationScale,"Select an ordinary exact non-annotative MText-content MLeader without Fields.");
        Need(leader.LeaderLineType==LeaderType.StraightLeader&&leader.Normal.IsEqualTo(Vector3d.ZAxis,Tolerance.Global),"This native vertex route requires straight WCS XY leader geometry.","OUT_OF_SCOPE");
        using var text=CadAnnotationTransformEditor.OwnedText(leader);Need(text!=null&&!text.HasFields&&text.ColumnType==ColumnType.NoColumns,"Field or column-layout content requires its owning adapter.");
        Need(text.Normal.IsEqualTo(Vector3d.ZAxis,Tolerance.Global),"Native embedded text is outside the retained XY plane.","OUT_OF_SCOPE");
        _=P(leader.TextLocation);_=P(text.Location);
        var lines=new List<MLeaderLineRead388>();var lineStyles=new List<object>();var seenBranches=new HashSet<int>();var seenLines=new HashSet<int>();var budget=new SerializedCapacity357(cancel:cancel);
        foreach(int branch in leader.GetLeaderIndexes())
        {
            cancel();Need(branch>=0&&seenBranches.Add(branch),"Native branch inventory is invalid.","NATIVE_MLEADER_READ_FAILED");
            foreach(int line in leader.GetLeaderLineIndexes(branch))
            {
                cancel();Need(line>=0&&seenLines.Add(line),"Native line inventory is invalid.","NATIVE_MLEADER_READ_FAILED");
                var lineType=leader.GetLeaderLineType(line);Need(lineType==LeaderType.StraightLeader,"This ordinary straight leader route does not edit a source with spline/non-line native per-line overrides.","OUT_OF_SCOPE");
                using var color=leader.GetLeaderLineColor(line);double arrowSize=leader.GetArrowSize(line);Need(double.IsFinite(arrowSize),"Native per-line arrow size is nonfinite.","INVALID_GEOMETRY");
                var lineStyle=new{leader_line_index=line,line_type=lineType.ToString(),line_type_handle=Id(leader.GetLeaderLineTypeId(line)),line_weight=(int)leader.GetLeaderLineWeight(line),
                    color=new{method=color.ColorMethod.ToString(),aci=color.ColorIndex,rgb=color.IsByColor?new[]{(int)color.Red,color.Green,color.Blue}:null},arrow_size=arrowSize,arrow_symbol_handle=Id(leader.GetArrowSymbolId(line))};
                budget.Add(lineStyle);lineStyles.Add(lineStyle);
                int count=leader.VerticesCount(line);Need(count>=2,"Native straight leader line requires at least two vertices.","INVALID_GEOMETRY");
                var points=new List<double[]>();for(int i=0;i<count;i++){cancel();var point=P(leader.GetVertex(line,i));budget.Add(point);if(i>0)Need(!Near(P(points[i-1]),P(point)),"Native leader segment is collapsed.","DEGENERATE_GEOMETRY");points.Add(point);}
                var value=new MLeaderLineRead388(branch,line,points.ToArray());budget.Add(value);lines.Add(value);
            }
        }
        Need(lines.Count>0,"Native MLeader has no editable line.","NATIVE_MLEADER_READ_FAILED");
        // Reuse the existing style-consumer observed paragraph/background reader.
        // Native direction/attachment belong to layout and may reflow at a landing;
        // they are observed below, not made a private-layout equality oracle.
        var embedded=JsonNode.Parse(JsonSerializer.Serialize(CadMLeaderStyleWriter337.EmbeddedText(text,cancel),Wire.Json))!.AsObject();
        embedded.Remove("normal");embedded.Remove("direction");embedded.Remove("attachment");
        var content=Wire.Element(new{content_type=leader.ContentType.ToString(),contents=text.Contents,style=Id(leader.MLeaderStyle),text_style=Id(text.TextStyleId),embedded_text_height=text.TextHeight,arrow_symbol=Id(leader.ArrowSymbolId),
            effective=CadMLeaderStyleWriter337.Effective(leader,cancel),line_styles=lineStyles,embedded});
        var result=new Geometry(content,lines.ToArray(),CadMLeaderStyleConsumerLayout349.Capture(leader,text,cancel),Display(leader));SerializedCapacity357.Check(result,cancel:cancel);return result;
    }
    internal static JsonElement Metadata(MLeader source,Transaction tr,Action cancel)
    {
        using var data=source.XData;using var links=source.GetPersistentReactorIds();var budget=new SerializedCapacity357(cancel:cancel);var reactors=new List<string?>();
        foreach(ObjectId id in links){cancel();var value=Id(id);budget.Add(value);reactors.Add(value);}
        var result=Wire.Element(new{root=new{handle=Id(source.ObjectId),owner=Id(source.OwnerId),runtime_type=source.GetType().FullName,extension=Id(source.ExtensionDictionary),
            has_fields=source.HasFields,annotative=source.Annotative.ToString(),xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,value=>budget.Add(value)),reactors},owned=NativeOwnedMetadata.Capture(source,tr,cancel)});
        SerializedCapacity357.Check(result,cancel:cancel);return result;
    }
    internal static void Ownership(MLeader source,Transaction tr,Action cancel)
    {
        cancel();Need(!source.ObjectId.IsNull&&source.ObjectId.IsValid&&!source.ObjectId.IsErased&&!source.ObjectId.IsEffectivelyErased,"Expected live resident MLeader.","OBJECT_NOT_FOUND");
        var db=source.Database;Need(HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Database==db,"Native leader document is not active.","DOCUMENT_CONTEXT_MISMATCH");
        var space=CadBlockSpace.RequireSpace(db,tr,source.OwnerId);Need(CadBlockSpace.RequireWriteSpace(db,tr,space.Kind,cancel)==source.OwnerId,"Leader is outside active ordinary Model/Paper.","OUT_OF_SCOPE");
        CadAnnotationTransformEditor.CheckSource(source,tr,cancel);
        Need(!source.MLeaderStyle.IsNull&&tr.GetObject(source.MLeaderStyle,OpenMode.ForRead) is MLeaderStyle style&&style.GetType()==typeof(MLeaderStyle)&&style.OwnerId==db.MLeaderStyleDictionaryId&&!style.Name.Contains('|'),"Foreign/unresolved native leader style requires its owning adapter.","STYLE_UNAVAILABLE");
        using var text=CadAnnotationTransformEditor.OwnedText(source);Need(text!=null&&!text.TextStyleId.IsNull&&tr.GetObject(text.TextStyleId,OpenMode.ForRead) is TextStyleTableRecord font&&!font.IsDependent&&!font.Name.Contains('|')&&font.OwnerId==db.TextStyleTableId,"Foreign/unresolved native MText font requires its owning adapter.","STYLE_UNAVAILABLE");
    }
    internal static State Capture(MLeader source,Transaction tr,Action cancel)
    {var result=new State(CaptureGeometry(source,cancel),Metadata(source,tr,cancel));SerializedCapacity357.Check(result,cancel:cancel);return result;}
    internal static (MLeaderGeometryRead388? Value,string? Reason) TryRead(MLeader source,Action cancel)
    {
        try
        {
            var tr=source.Database.TransactionManager.TopTransaction??throw new DomainException("OBJECT_READ_FAILED","Native leader token requires current read transaction.");
            var state=Capture(source,tr,cancel);var value=new MLeaderGeometryRead388(MLeaderGeometryValidation388.Token(state,cancel),true,state.Geometry.Lines,state.Geometry.Content.GetProperty("line_styles"),state.Geometry.Layout,
                "Complete actual native branch/line/vertex inventory and observed layout for ordinary straight XY MText leaders. A read token alone does not establish independent ownership or write admission.");
            SerializedCapacity357.Check(value,Wire.PayloadBudget-8192,cancel);return(value,null);
        }
        catch(OperationCanceledException){throw;}
        catch(DomainException e){cancel();return(null,e.Error.code+": "+e.Message);}
        catch(Teigha.Runtime.Exception e){cancel();return(null,"NATIVE_MLEADER_READ_UNAVAILABLE: "+e.ErrorStatus);}
        catch(Exception e){cancel();return(null,"NATIVE_MLEADER_READ_UNAVAILABLE: "+e.GetType().Name);}
    }
    private static MLeaderLineRead388 Selected(Geometry before,MLeaderGeometryEdit388 edit)
    {
        var line=before.Lines.SingleOrDefault(l=>l.leader_line_index==edit.LeaderLineIndex);Need(line!=null,"Select an actual current native leader_line_index.","MLEADER_LINE_NOT_FOUND");
        foreach(var vertex in edit.Vertices)Need(vertex.Index<line.vertices_wcs.Length,"Selected vertex is absent from current native line.","MLEADER_VERTEX_NOT_FOUND");return line;
    }
    private static JsonElement Placement(JsonElement layout)
    {
        var node=JsonNode.Parse(layout.GetRawText())!;
        foreach(var branch in node["branches"]!.AsArray())branch!.AsObject().Remove("lines");
        return Wire.Element(node);
    }
    private static State Prepare(MLeader source,MLeaderGeometryEdit388 edit,Transaction tr,Action cancel)
    {
        Ownership(source,tr,cancel);var before=Capture(source,tr,cancel);Need(MLeaderGeometryValidation388.Token(before,cancel)==edit.ExpectedGeometryToken,"Native leader content/geometry/owner/layout changed; inspect its current complete token.","GEOMETRY_CHANGED");_=Selected(before.Geometry,edit);return before;
    }
    internal static void CheckSource(Entity source,MLeaderGeometryEdit388 edit,Transaction tr,Action cancel)
    {Need(source.GetType()==typeof(MLeader),"Native vertex edit requires an exact MLeader.","UNSUPPORTED_ENTITY_TYPE");_=Prepare((MLeader)source,edit,tr,cancel);}
    private static string[] Run(MLeader source,Geometry before,MLeaderGeometryEdit388 edit,Action cancel)
    {
        var line=Selected(before,edit);int last=line.vertices_wcs.Length-1;bool landing=edit.Vertices.Any(v=>v.Index==last);
        // Apply interior and arrow first; a requested landing is last. Verification
        // reads every requested point after the complete native group, not each setter.
        foreach(var vertex in edit.Vertices.OrderBy(v=>v.Index==last?1:0).ThenBy(v=>v.Index))
        {
            cancel();var point=new Point3d(vertex.PointWcs.X,vertex.PointWcs.Y,vertex.PointWcs.Z);
            if(vertex.Index==0)source.SetFirstVertex(edit.LeaderLineIndex,point);
            else if(vertex.Index==last)source.SetLastVertex(edit.LeaderLineIndex,point);
            else source.SetVertex(edit.LeaderLineIndex,vertex.Index,point);
        }
        cancel();if(!source.ObjectId.IsNull)source.RecordGraphicsModified(true);var after=CaptureGeometry(source,cancel);
        Need(MLeaderTextVerification.Unchanged(before.Content,after.Content)&&Same(before.Display,after.Display),"Native vertex edit changed retained text/style/display properties.","NATIVE_MLEADER_GEOMETRY_MISMATCH");
        Need(before.Lines.Length==after.Lines.Length&&before.Lines.All(old=>after.Lines.Any(current=>current.leader_index==old.leader_index&&current.leader_line_index==old.leader_line_index&&current.vertices_wcs.Length==old.vertices_wcs.Length)),"Native vertex edit changed branch/line/vertex topology.","NATIVE_MLEADER_GEOMETRY_MISMATCH");
        var actual=Selected(after,edit);foreach(var vertex in edit.Vertices){cancel();Need(Near(P(actual.vertices_wcs[vertex.Index]),new(vertex.PointWcs.X,vertex.PointWcs.Y,vertex.PointWcs.Z)),"A requested native leader vertex did not read back.","NATIVE_MLEADER_GEOMETRY_MISMATCH");}
        Need(landing||SamePoint(before.Layout.GetProperty("text_location"),after.Layout.GetProperty("text_location")),"Arrow/interior-only retarget moved the required retained native TextLocation.","NATIVE_MLEADER_GEOMETRY_MISMATCH");
        int adjusted=0;string? first=null;var selected=edit.Vertices.Select(v=>v.Index).ToHashSet();
        foreach(var old in before.Lines)
        {
            var current=after.Lines.Single(l=>l.leader_line_index==old.leader_line_index);
            for(int i=0;i<old.vertices_wcs.Length;i++){cancel();if(old.leader_line_index==edit.LeaderLineIndex&&selected.Contains(i)||Near(P(old.vertices_wcs[i]),P(current.vertices_wcs[i])))continue;adjusted++;first??="line "+old.leader_line_index+", vertex "+i;}
        }
        var notices=new List<string>{Warning};
        if(adjusted>0)notices.Add("NATIVE_MLEADER_UNSELECTED_ADJUSTMENT: Native layout adjusted "+adjusted+" unselected vertex/vertices; first "+first+". Requested points and retained content/style passed; inspect all actual branches in the next read.");
        if(!MLeaderTextVerification.Unchanged(Placement(before.Layout),Placement(after.Layout)))notices.Add("NATIVE_MLEADER_LAYOUT_ADJUSTMENT: Native text/dogleg/landing layout changed. "+(landing?"Last-endpoint selection permits this native reflow. ":"TextLocation stayed retained. ")+"Read geometry.leader_editing.layout for actual placement; no pixel/layout oracle was imposed.");
        return notices.ToArray();
    }
    private static void Output(ModifiedEntities result,string[] warnings,Action cancel)
    {SerializedCapacity357.Check(new{data=result,warnings=warnings.Concat(new[]{WriteRecovery.Warning}).ToArray()},Wire.PayloadBudget-16384,cancel);}
    internal static void Preview(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,Action cancel)
    {
        var edit=(MLeaderGeometryEdit388)args.Operations.Single();var source=(MLeader)tr.GetObject(resolved.Sources[edit.Handles[0]],OpenMode.ForRead);var before=Prepare(source,edit,tr,cancel);
        using var clone=CadContourBudget344.Detached(source);var preview=CaptureGeometry((MLeader)clone,cancel);
        Need(MLeaderTextVerification.Unchanged(before.Geometry.Content,preview.Content)&&Same(before.Geometry.Display,preview.Display)&&MLeaderTextVerification.Unchanged(Wire.Element(before.Geometry.Lines),Wire.Element(preview.Lines)),"Detached native leader did not retain the source content/style/display and complete native vertex geometry for preview.","UNSAFE_NATIVE_CLONE");
        // The clone's cached layout need not be byte-identical. Requested sparse
        // geometry and retained-anchor policy still compare with the actual source.
        var warnings=Run((MLeader)clone,before.Geometry,edit,cancel);Output(Result(args,edit),warnings,cancel);
        var after=Capture(source,tr,cancel);Need(MLeaderGeometryValidation388.Token(before,cancel)==MLeaderGeometryValidation388.Token(after,cancel),"Native preview changed its resident source.","NATIVE_MLEADER_GEOMETRY_MISMATCH");
    }
    private static ModifiedEntities Result(ModifyArguments args,MLeaderGeometryEdit388 edit)=>new(args.OperationId,"committed",false,[new(args.BridgeId,args.DocumentId,edit.Handles[0])],[],[],[],"not_guaranteed");
    internal static ModifiedEntities Apply(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,WriteJob job,List<string>? warnings)
    {
        var edit=(MLeaderGeometryEdit388)args.Operations.Single();var source=(MLeader)tr.GetObject(resolved.Sources[edit.Handles[0]],OpenMode.ForRead);
        Need(source.OwnerId==resolved.Model&&CadBlockSpace.RequireWriteSpace(source.Database,tr,args.TargetSpace,job.CheckCancellation)==resolved.Model,"Native leader differs from requested active space.","OUT_OF_SCOPE");
        var before=Prepare(source,edit,tr,job.CheckCancellation);var result=Result(args,edit);Output(result,[],job.CheckCancellation);
        source.UpgradeOpen();var notices=Run(source,before.Geometry,edit,job.CheckCancellation);var after=Capture(source,tr,job.CheckCancellation);
        Need(Same(before.Metadata,after.Metadata),"Native edit changed root/owner/owned application data.","NATIVE_MLEADER_GEOMETRY_MISMATCH");Output(result,notices,job.CheckCancellation);warnings?.AddRange(notices);return result;
    }
}
