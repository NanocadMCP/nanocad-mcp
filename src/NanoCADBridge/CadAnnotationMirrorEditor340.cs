using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Geometric reflection through the native object's adapter. No reconstruction
// of cells, glyphs, hatch curves, Region boundaries or Helix control geometry.
internal static class CadAnnotationMirrorEditor340
{
    private const string Warning = "Native mirror reflects the object's definition; text orientation and automatic landing/layout follow its native adapter. Command-level MIRRTEXT readability is not emulated. Inspect text readability when the task needs it. Recovery/Undo is not guaranteed.";
    private static void Need([DoesNotReturnIf(false)] bool condition,string message,string code="NATIVE_ANNOTATION_MIRROR_MISMATCH")
    {if(!condition)throw new DomainException(code,message);}
    private static string? Id(ObjectId value)=>value.IsNull?null:Validation.NormalizeHandle(value.Handle.ToString());
    private static double[] P(Point3d p)=>new[]{p.X,p.Y,p.Z};
    private static double[] V(Vector3d p)=>new[]{p.X,p.Y,p.Z};
    private static bool Exact(object? left,object? right)=>JsonSerializer.Serialize(left,Wire.Json)==JsonSerializer.Serialize(right,Wire.Json);
    private static void Budget(object value,Action cancel)=>new ModelInventoryBudget(cancel).Add(value);
    internal static bool Supported(Type type)=>CadAnnotationTransformEditor.Supported(type)&&!CadExtendedDimensions356.Supported(type);
    internal static string[] Warnings()=>new[]{Warning};

    // Clone must be proved detached before registering it for disposal. In
    // particular no resident alias is ever disposed on a failed SDK getter.
    internal static Entity Detached(Entity source)
    {
        var entity=source.Clone() as Entity;
        Need(entity!=null&&entity.GetType()==source.GetType()&&entity.ObjectId.IsNull,
            "Native annotation clone did not return a detached exact entity.","UNSAFE_NATIVE_CLONE");
        return entity;
    }
    private static MText? OwnedText(MLeader source)
        =>CadAnnotationTransformEditor.OwnedText(source);
    private static object Color(Teigha.Colors.Color color)=>new{method=color.ColorMethod.ToString(),color.ColorIndex,color.Red,color.Green,color.Blue};
    private static JsonElement XData(DBObject source,Action cancel)
    {
        using var data=source.XData;var budget=new ModelInventoryBudget(cancel);
        return JsonSerializer.SerializeToElement(data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add),Wire.Json);
    }
    private static JsonElement ReflectedXData(Entity source,Matrix3d? matrix,Action cancel)
    {
        var data=XData(source,cancel);
        if(matrix==null||data.ValueKind==JsonValueKind.Null)return data;
        using var expected=Detached(source);expected.XDataTransformBy(matrix.Value);return XData(expected,cancel);
    }
    internal static JsonElement Metadata(Entity source,Transaction tr,Action cancel)
    {
        var owned=NativeOwnedMetadata.Capture(source,tr,cancel);
        // Native external dependency graphs are not ordinary metadata, even
        // when they sit below a nested extension dictionary.
        foreach(var record in owned.EnumerateArray())
        {cancel();Need(record.GetProperty("common").GetProperty("reactors").GetArrayLength()==0,
            "Owned persistent dependency data needs its owning adapter.","UNSUPPORTED_ANNOTATION_DEPENDENCIES");}
        return owned;
    }

    private static object TableFormats(Table table,Action cancel)
    {
        // Observe defaults before the final complete per-cell override pass.
        string flow=table.FlowDirection.ToString();double horizontalMargin=table.HorizontalCellMargin,verticalMargin=table.VerticalCellMargin;
#pragma warning disable CS0618
        bool titleSuppressed=table.IsTitleSuppressed,headerSuppressed=table.IsHeaderSuppressed;
#pragma warning restore CS0618
        var cells=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(int row in Enumerable.Range(0,table.Rows.Count))foreach(int column in Enumerable.Range(0,table.Columns.Count))
        {
            cancel();var merged=CadTableEditingReader.MergedRange(table,row,column,cancel);
            bool anchor=merged==null||merged.TopRow==row&&merged.LeftColumn==column;
            var contents=new List<object>();var contentBudget=new ModelInventoryBudget(cancel);
            int count=table.Cells[row,column].Contents.Count;
            for(int index=0;index<count;index++)
            {
                cancel();var kind=table.GetContentTypes(row,column,index);object item;
                var nativeContent=table.Cells[row,column].Contents[index];bool automatic=nativeContent.IsAutoScale;
                if(kind==CellContentTypes.Value)
                {
                    using var color=table.GetContentColor(row,column,index);
                    item=new{index,data_format=table.GetDataFormat(row,column,index),text_style=Id(table.GetTextStyleId(row,column,index)),
                        text_height=table.GetTextHeight(row,column,index),color=Color(color),overrides=(int)table.GetOverrides(row,column,index),
                        auto_scale=automatic,manual_scale=automatic?(double?)null:nativeContent.Scale};
                }
                else item=new{index,kind=kind.ToString(),block=kind==CellContentTypes.Block?Id(table.GetBlockTableRecordId(row,column,index)):null,
                    overrides=(int)table.GetOverrides(row,column,index),auto_scale=automatic,manual_scale=automatic?(double?)null:nativeContent.Scale};
                contentBudget.Add(item);contents.Add(item);
            }
            object? format=null;
            if(anchor)
            {
                using var color=table.ContentColor(row,column);using var background=table.BackgroundColor(row,column);
                var grids=new List<object>();
                foreach(var line in new[]{GridLineType.HorizontalTop,GridLineType.HorizontalInside,GridLineType.HorizontalBottom,GridLineType.VerticalLeft,GridLineType.VerticalInside,GridLineType.VerticalRight})
                {
                    cancel();using var gridColor=table.GetGridColor(row,column,line);
                    grids.Add(new{line=line.ToString(),visible=table.GetGridVisibility(row,column,line).ToString(),
                        weight=(int)table.GetGridLineWeight(row,column,line),linetype=Id(table.GetGridLinetype(row,column,line)),
                        style=table.GetGridLineStyle(row,column,line).ToString(),spacing=table.GetGridDoubleLineSpacing(row,column,line),
                        overrides=(int)table.GetOverrides(row,column,line),color=Color(gridColor)});
                }
                format=new{cell_style=table.GetCellStyle(row,column),alignment=table.Alignment(row,column).ToString(),
                    data_format=table.GetDataFormat(row,column),text_height=table.TextHeight(row,column),text_style=Id(table.TextStyle(row,column)),
                    color=Color(color),background_none=table.IsBackgroundColorNone(row,column),background=Color(background),
                    margins=new[]{CellMargins.Top,CellMargins.Left,CellMargins.Bottom,CellMargins.Right}.Select(m=>table.GetMargin(row,column,m)).ToArray(),grids};
            }
            var cell=new{row,column,contents,format,overrides=table.CellStyleOverrides(row,column).Select(v=>(int)v).OrderBy(v=>v).ToArray()};
            budget.Add(cell);cells.Add(cell);
        }
        // Mirror supports native multiple/block contents beyond the value-edit
        // subset. Retain that complete typed state; only defer its full native
        // flag inventory until all format/default getters have been observed.
        var completedCells=new List<JsonNode>();var completedBudget=new ModelInventoryBudget(cancel);
        foreach(var item in cells)
        {
            cancel();var cell=JsonSerializer.SerializeToNode(item,Wire.Json)!.AsObject();
            int row=cell["row"]!.GetValue<int>(),column=cell["column"]!.GetValue<int>();
            foreach(var content in cell["contents"]!.AsArray())
            {cancel();content!["overrides"]=(int)table.GetOverrides(row,column,content["index"]!.GetValue<int>());}
            if(cell["format"] is JsonObject format)foreach(var grid in format["grids"]!.AsArray())
            {
                cancel();Need(Enum.TryParse<GridLineType>(grid!["line"]!.GetValue<string>(),out var line),"Captured native grid identity is unavailable.");
                grid["overrides"]=(int)table.GetOverrides(row,column,line);
            }
            cell["overrides"]=JsonSerializer.SerializeToNode(table.CellStyleOverrides(row,column).Select(v=>(int)v).OrderBy(v=>v).ToArray(),Wire.Json);
            completedBudget.Add(cell);completedCells.Add(cell);
        }
        var result=new{table_overrides=table.TableStyleOverrides().Select(v=>(int)v).OrderBy(v=>v).ToArray(),
            flow,HorizontalCellMargin=horizontalMargin,VerticalCellMargin=verticalMargin,IsTitleSuppressed=titleSuppressed,IsHeaderSuppressed=headerSuppressed,cells=completedCells};
        Budget(result,cancel);return result;
    }
    private static Dictionary<string,double> AutomaticScales(Entity source,Action cancel)
    {
        var result=new Dictionary<string,double>();var budget=new ModelInventoryBudget(cancel);
        if(source is Table table)for(int row=0;row<table.Rows.Count;row++)for(int column=0;column<table.Columns.Count;column++)
            for(int index=0;index<table.Cells[row,column].Contents.Count;index++)
            {
                cancel();var content=table.Cells[row,column].Contents[index];if(!content.IsAutoScale)continue;
                string key=$"{row},{column},{index}";double scale=content.Scale;
                Need(double.IsFinite(scale),"Native automatic table content scale is nonfinite.","INVALID_GEOMETRY");
                budget.Add(new{key,scale});result.Add(key,scale);
            }
        return result;
    }
    private static object TextStored(MText text,Action cancel,Matrix3d? matrix)
    {
        using var color=text.Color;using var background=text.BackgroundFillColor;
        var columns=new List<double>();var columnBudget=new ModelInventoryBudget(cancel);
        bool staticColumns=text.ColumnType==ColumnType.StaticColumns;
        if(staticColumns)for(int i=0;i<text.ColumnCount;i++){cancel();double height=text.GetColumnHeight(i);columnBudget.Add(height);columns.Add(height);}
        var result=new{text.Contents,text_style=Id(text.TextStyleId),text.TextHeight,text.Width,color=Color(color),
            text.LineSpacingFactor,line_spacing=text.LineSpacingStyle.ToString(),flow=text.FlowDirection.ToString(),
            text.BackgroundFill,text.UseBackgroundColor,text.BackgroundScaleFactor,background_color=Color(background),
            transparency=new{alpha=text.BackgroundTransparency.Alpha,text.BackgroundTransparency.IsByAlpha,text.BackgroundTransparency.IsByLayer,text.BackgroundTransparency.IsByBlock},
            column_type=text.ColumnType.ToString(),text.ColumnWidth,text.ColumnGutterWidth,text.ColumnAutoHeight,text.ColumnFlowReversed,
            explicit_column_count=staticColumns?(int?)text.ColumnCount:null,explicit_column_heights=columns,
            dynamic_fixed_height=text.ColumnType==ColumnType.DynamicColumns&&!text.ColumnAutoHeight?(double?)text.Height:null,
            xdata=ReflectedXData(text,matrix,cancel),extension=Id(text.ExtensionDictionary)};
        Budget(result,cancel);return result;
    }
    private static object? LeaderContent(MLeader leader,Action cancel,Matrix3d? matrix)
    {
        if(leader.ContentType==ContentType.MTextContent)
        {using var text=OwnedText(leader);Need(text!=null&&!text.HasFields,"Native leader text is unavailable/field-bearing.","UNSUPPORTED_ANNOTATION_DEPENDENCIES");return TextStored(text,cancel,matrix);}
        return null;
    }
    private static object LeaderStored(MLeader leader,Action cancel)
    {
        using var line=leader.LeaderLineColor;using var text=leader.TextColor;
        var topology=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(int branch in leader.GetLeaderIndexes())
        {
            cancel();var lines=new List<object>();var lineBudget=new ModelInventoryBudget(cancel);
            foreach(int index in leader.GetLeaderLineIndexes(branch))
            {cancel();int vertices=leader.VerticesCount(index);Need(vertices>=0,"Native leader vertex count is invalid.","INVALID_GEOMETRY");var item=new{index,vertices};lineBudget.Add(item);lines.Add(item);}
            var itemBranch=new{branch,lines};budget.Add(itemBranch);topology.Add(itemBranch);
        }
        return new{leader.Scale,leader.TextHeight,leader.ArrowSize,leader.LandingGap,leader.DoglegLength,
            leader.EnableLanding,leader.EnableDogleg,line_color=Color(line),text_color=Color(text),
            linetype=Id(leader.LeaderLineTypeId),weight=(int)leader.LeaderLineWeight,topology};
    }
    private static JsonElement Content(Entity source,Transaction tr,Action cancel,Matrix3d? matrix=null)
    {
        object? detail=source switch{Table table=>TableFormats(table,cancel),MLeader initialLeader=>LeaderContent(initialLeader,cancel,matrix),_=>null};
        if(source is MLeader blockLeader&&blockLeader.ContentType==ContentType.BlockContent)
        {
            var attributes=new List<object>();var budget=new ModelInventoryBudget(cancel);
            var definition=(BlockTableRecord)tr.GetObject(blockLeader.BlockContentId,OpenMode.ForRead);
            foreach(ObjectId id in definition)
            {
                cancel();if(tr.GetObject(id,OpenMode.ForRead) is not AttributeDefinition attribute||attribute.Constant)continue;
                var value=blockLeader.GetBlockAttribute(id);
                Need(value==null||value.ObjectId.IsNull,"Native MLeader attribute getter returned a resident alias.","UNSAFE_NATIVE_CLONE");
                using(value)
                {
                    var item=new{definition=Id(id),text=value?.TextString,style=value==null?null:Id(value.TextStyleId),height=value?.Height,width_factor=value?.WidthFactor,oblique=value?.Oblique,
                        xdata=value==null?default(JsonElement?):ReflectedXData(value,matrix,cancel),extension=value==null?null:Id(value.ExtensionDictionary)};
                    budget.Add(item);attributes.Add(item);
                }
            }
            detail=attributes;
        }
        var result=Wire.Element(new{stored=CadAnnotationTransformEditor.Content(source,cancel,reflection:true),detail,
            leader_stored=source is MLeader storedLeader?LeaderStored(storedLeader,cancel):null});
        Budget(result,cancel);return result;
    }
    private static Dictionary<string,Point3d> Anchors(Entity source,Action cancel)
    {
        var result=CadAnnotationTransformEditor.Anchors(source,cancel);
        if(source is MLeader leader)
        {
            // Native text orientation can reflow a terminal landing. Preserve
            // the measured target/middle vertices, not derived connection points.
            foreach(int branch in leader.GetLeaderIndexes())foreach(int line in leader.GetLeaderLineIndexes(branch))
            {cancel();int count=leader.VerticesCount(line);if(count>1)result.Remove($"leader/{branch}/{line}/{count-1}");}
        }
        if(source is Helix helix)
        {
            double a=helix.StartParam,b=helix.EndParam,step=Math.Min(.25,.25/helix.Turns);
            Need(double.IsFinite(a)&&double.IsFinite(b)&&b>a,"Native Helix parameter interval is unavailable.","INVALID_GEOMETRY");
            foreach(double fraction in new[]{0,step,2*step,3*step,1})
            {cancel();result.Add("helix_sample/"+fraction.ToString("R",System.Globalization.CultureInfo.InvariantCulture),helix.GetPointAtParameter(a+(b-a)*fraction));}
        }
        Budget(result.ToDictionary(v=>v.Key,v=>P(v.Value)),cancel);return result;
    }
    internal static void CheckSource(Entity source,Transaction tr,Action cancel)
    {
        Need(Supported(source.GetType()),"Expected an exact ordinary native annotation, Region or Helix.","UNSUPPORTED_ENTITY_TYPE");
        var owned=Metadata(source,tr,cancel);
        CadAnnotationTransformEditor.CheckSource(source,tr,cancel);
        var content=Content(source,tr,cancel);var data=XData(source,cancel);
        Budget(new{owned,content,data},cancel);
    }
    private static void VerifyAnchors(Dictionary<string,Point3d> before,Entity after,Matrix3d matrix,Action cancel)
    {
        var actual=Anchors(after,cancel);
        Need(before.Keys.ToHashSet().SetEquals(actual.Keys),"Native mirror changed measured point/leader topology.");
        foreach(var pair in before)
        {
            cancel();var expected=pair.Value.TransformBy(matrix);double tolerance=1e-8+1e-10*Math.Max(1,expected.DistanceTo(Point3d.Origin));
            Need(expected.DistanceTo(actual[pair.Key])<=tolerance,"Native mirror did not reflect a measured defining point/Helix sample.");
        }
    }
    private static Dictionary<string,Vector3d> Frame(Entity source)
    {
        var frame=new Dictionary<string,Vector3d>();
        if(source is Table table)
        {
            // The actual INSERT frame includes native negative scale signs;
            // Direction alone does not establish a mirrored cell basis.
            var transform=table.BlockTransform;frame.Add("table_x",Vector3d.XAxis.TransformBy(transform));frame.Add("table_y",Vector3d.YAxis.TransformBy(transform));
        }
        var normal=source switch{Dimension d=>(Vector3d?)d.Normal,MLeader l=>l.Normal,Hatch h=>h.Normal,Region r=>r.Normal,_=>null};
        if(normal is {} n)frame.Add("unoriented_plane",n);
        return frame;
    }
    private static void VerifyFrame(Dictionary<string,Vector3d> before,Entity source,Matrix3d matrix,Action cancel)
    {
        var after=Frame(source);Need(before.Keys.ToHashSet().SetEquals(after.Keys),"Native mirror changed the observed frame type.");
        foreach(var pair in before)
        {
            cancel();var expected=pair.Value.TransformBy(matrix);var actual=after[pair.Key];
            Need(double.IsFinite(actual.Length)&&actual.Length>0&&double.IsFinite(expected.Length)&&expected.Length>0,"Native mirrored frame is degenerate.");
            double tolerance=1e-8+1e-10*Math.Max(1,expected.Length);
            // A plane normal may change sign when its oriented parameter basis
            // reverses; this is not loss of the physical plane.
            double distance=(expected-actual).Length;
            if(pair.Key=="unoriented_plane")distance=Math.Min(distance,(expected+actual).Length);
            Need(distance<=tolerance,"Native mirror did not retain the reflected cell frame/annotation plane.");
        }
    }
    // Native handle translation, including typed ObjectId references and DXF
    //1005 xdata handles. Ordinary string payloads are never rewritten by guessing.
    internal static JsonElement Remap(JsonElement value,Dictionary<string,string> map,Action cancel)
    {
        var root=JsonNode.Parse(value.GetRawText());var pending=new Stack<JsonNode?>();pending.Push(root);
        while(pending.Count>0)
        {
            cancel();
            var current=pending.Pop();
            if(current is JsonObject record)
            {
                foreach(var name in new[]{"handle","owner_handle","extension_handle","extension"})
                    if(record[name] is JsonValue token&&token.TryGetValue<string>(out var handle)&&map.TryGetValue(handle,out var mapped))record[name]=mapped;
                if(record["code"] is JsonValue code&&code.TryGetValue<int>(out var kind)&&kind==1005&&record["value"] is JsonValue text&&
                    text.TryGetValue<string>(out var old)&&map.TryGetValue(old,out var replacement))record["value"]=replacement;
                bool retainsRecordReferences=record["xlate_references"] is JsonValue xlate&&xlate.TryGetValue<bool>(out bool translates)&&!translates;
                foreach(var property in record)
                    // Xrecord.Data reference translation is explicitly native
                    // opt-in. Structural ownership/common XData still use
                    // IdMapping; never invent translated stored record data.
                    if(property.Key!="data"||!retainsRecordReferences)pending.Push(property.Value);
            }
            else if(current is JsonArray array)foreach(var child in array)pending.Push(child);
        }
        var result=JsonSerializer.SerializeToElement(root,Wire.Json);Budget(result,cancel);return result;
    }
    private sealed record PreservationField(string path,string? expected_sha256,string? current_sha256);
    private static object TablePreservationDetails(JsonElement expected,JsonElement? current,string? sourceHandle,
        bool detached,bool identityRetained,bool ownerReached,bool? ownerRetained,bool contentReached,bool? contentRetained,Action cancel)
    {
        // Diagnostic only: consume completed managed snapshots, never a native
        // wrapper/getter. Fingerprints do not authorize writes or relax guards.
        const int detailBytes=8192;const int traversalBytes=2*1024*1024;
        string? Hash(JsonElement? value)=>value.HasValue?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(value.Value,Wire.Json))):null;
        try
        {
            var fields=new List<PreservationField>();int bytes=1536;long different=0,omitted=0;
            var pending=new Stack<(string Path,JsonElement? Expected,JsonElement? Current)>();int reserved=0;
            void Push(string path,JsonElement? before,JsonElement? after)
            {
                cancel();int size=JsonSerializer.SerializeToUtf8Bytes(new{path,expected_present=before.HasValue,current_present=after.HasValue},Wire.Json).Length;
                if(size>traversalBytes-reserved)throw new InvalidOperationException("Mirror fingerprint traversal budget exceeded.");
                reserved+=size;pending.Push((path,before,after));
            }
            bool Same(JsonElement? a,JsonElement? b)=>a.HasValue==b.HasValue&&(!a.HasValue||Exact(a.Value,b!.Value));
            void Add(string path,JsonElement? before,JsonElement? after)
            {
                cancel();different++;var field=new PreservationField(path,Hash(before),Hash(after));
                int size=JsonSerializer.SerializeToUtf8Bytes(field,Wire.Json).Length+1;
                if(size>detailBytes-bytes){omitted++;return;}bytes+=size;fields.Add(field);
            }
            if(current.HasValue)Push("content",expected,current);
            while(pending.Count!=0)
            {
                cancel();var item=pending.Pop();if(Same(item.Expected,item.Current))continue;
                if(item.Expected.HasValue&&item.Current.HasValue&&item.Expected.Value.ValueKind==JsonValueKind.Object&&item.Current.Value.ValueKind==JsonValueKind.Object)
                {
                    foreach(var property in item.Expected.Value.EnumerateObject())
                    {
                        cancel();JsonElement? actual=item.Current.Value.TryGetProperty(property.Name,out var found)?found:null;
                        Push(item.Path+"/"+property.Name.Replace("~","~0").Replace("/","~1"),property.Value,actual);
                    }
                    foreach(var property in item.Current.Value.EnumerateObject())
                    {cancel();if(!item.Expected.Value.TryGetProperty(property.Name,out _))Push(item.Path+"/"+property.Name.Replace("~","~0").Replace("/","~1"),null,property.Value);}
                }
                else if(item.Expected.HasValue&&item.Current.HasValue&&item.Expected.Value.ValueKind==JsonValueKind.Array&&item.Current.Value.ValueKind==JsonValueKind.Array)
                {
                    int count=Math.Max(item.Expected.Value.GetArrayLength(),item.Current.Value.GetArrayLength());
                    for(int index=0;index<count;index++)
                    {
                        cancel();Push(item.Path+"/"+index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            index<item.Expected.Value.GetArrayLength()?item.Expected.Value[index]:null,
                            index<item.Current.Value.GetArrayLength()?item.Current.Value[index]:null);
                    }
                }
                else Add(item.Path,item.Expected,item.Current);
            }
            var details=new{diagnostic="table_mirror_preservation_fingerprints",source_handle=sourceHandle,
                stage=detached?"detached_preview":"resident_transform",object_identity_check_reached=true,object_identity_retained=identityRetained,
                owner_check_reached=ownerReached,owner_retained=ownerRetained,content_check_reached=contentReached,content_retained=contentRetained,
                owned_metadata_check_reached=false,anchors_check_reached=false,frame_check_reached=false,
                expected_content_sha256=Hash(expected),current_content_sha256=Hash(current),changed_fields=fields,
                different_component_count=different,omitted_component_count=omitted,
                note="Fingerprints describe this completed Table content comparison only; later mirror checks were not reached."};
            if(JsonSerializer.SerializeToUtf8Bytes(details,Wire.Json).Length>detailBytes)throw new InvalidOperationException("Mirror fingerprint response budget exceeded.");
            return details;
        }
        catch(OperationCanceledException){throw;}
        catch(System.Exception)
        {
            return new{diagnostic="table_mirror_preservation_fingerprints",source_handle=sourceHandle,stage=detached?"detached_preview":"resident_transform",
                object_identity_check_reached=true,object_identity_retained=identityRetained,owner_check_reached=ownerReached,owner_retained=ownerRetained,
                content_check_reached=contentReached,content_retained=contentRetained,diagnostic_unavailable_reason="Completed managed fingerprint detail was unavailable or exceeded its byte budget."};
        }
    }
    internal static string[] Apply(Entity source,Mirror2dSpec operation,Transaction tr,Action cancel,string? diagnosticSourceHandle=null)
    {
        cancel();CadAnnotationTransformEditor.CheckGeometry(source,cancel);
        var id=source.ObjectId;var owner=source.OwnerId;var matrix=CadModifier.Matrix(operation);
        var content=Content(source,tr,cancel,matrix);JsonElement? owned=id.IsNull?null:Metadata(source,tr,cancel);
        var beforeData=XData(source,cancel);var anchors=Anchors(source,cancel);var frame=Frame(source);
        var automaticScales=AutomaticScales(source,cancel);
        var expectedData=ReflectedXData(source,matrix,cancel);
        source.TransformBy(matrix);
        var actualData=XData(source,cancel);
        // SDK adapters may or may not call the separate native XData method.
        // Complete it once only when the exact unchanged-before state proves
        // it has not happened; never apply a second reflection to changed data.
        if(!Exact(actualData,expectedData)&&Exact(actualData,beforeData))source.XDataTransformBy(matrix);
        if(!source.ObjectId.IsNull)
        {
            if(source is Dimension dimension)dimension.RecomputeDimensionBlock(true);
            else if(source is Table table){table.GenerateLayout();table.RecomputeTableBlock(true);}
            source.RecordGraphicsModified(true);
        }
        cancel();CadAnnotationTransformEditor.CheckGeometry(source,cancel);
        if(source is Table)
        {
            // Retain the original predicate's native getter order and short
            // circuit: do not capture content when identity/ownership failed.
            bool identityRetained=source.ObjectId==id;bool ownerReached=identityRetained;
            bool? ownerRetained=ownerReached?source.OwnerId==owner:null;
            bool contentReached=identityRetained&&ownerRetained==true;
            JsonElement? actualContent=contentReached?Content(source,tr,cancel):null;
            bool? contentRetained=contentReached?Exact(content,actualContent!.Value):null;
            if(!identityRetained||ownerRetained!=true||contentRetained!=true)
                throw new DomainException("NATIVE_ANNOTATION_MIRROR_MISMATCH","Native mirror changed stored content/style/format or root ownership.",false,
                    TablePreservationDetails(content,actualContent,diagnosticSourceHandle,id.IsNull,identityRetained,ownerReached,ownerRetained,contentReached,contentRetained,cancel));
        }
        else
            Need(source.ObjectId==id&&source.OwnerId==owner&&Exact(content,Content(source,tr,cancel)),"Native mirror changed stored content/style/format or root ownership.");
        Need((owned==null||Exact(owned.Value,Metadata(source,tr,cancel)))&&Exact(expectedData,XData(source,cancel)),"Native mirror changed owned metadata or failed native world-XData reflection.");
        VerifyAnchors(anchors,source,matrix,cancel);VerifyFrame(frame,source,matrix,cancel);
        var observedScales=AutomaticScales(source,cancel);int changes=0;string? first=null;
        foreach(var prior in automaticScales)
        {
            cancel();if(observedScales.TryGetValue(prior.Key,out double actual)&&actual!=prior.Value)
            {changes++;first??=prior.Key+": "+prior.Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" -> "+actual.ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
        }
        return changes==0?Warnings():new[]{Warning,$"Native automatic table content scale reflowed in {changes} contents (first row,column,content {first}); automatic flags and manual scales remain checked."};
    }
    internal static Entity Copy(Entity source,Mirror2dSpec operation,ObjectId targetOwner,Transaction tr,Action cancel,Action<string>? warning=null)
    {
        Need(operation.Copy,"Mirror copy requires copy=true.","INVALID_ARGUMENT");CheckSource(source,tr,cancel);
        var original=CadAnnotationTransformEditor.Snapshot(source,cancel);var data=XData(source,cancel);var metadata=Metadata(source,tr,cancel);
        var content=Content(source,tr,cancel);var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        using var ids=new ObjectIdCollection(new[]{source.ObjectId});using var mapping=new IdMapping();
        source.Database.DeepCloneObjects(ids,targetOwner,mapping,false);cancel();
        Need(mapping.Contains(source.ObjectId),"Native mirror copy returned no root mapping.");
        var root=mapping.Lookup(source.ObjectId);
        Need(!root.Value.IsNull&&root.Value.IsValid&&!root.Value.IsErased&&!root.Value.IsEffectivelyErased,"Native mirror copy root is unavailable.");
        var copy=tr.GetObject(root.Value,OpenMode.ForWrite) as Entity;
        Need(root.IsCloned&&root.Key==source.ObjectId&&root.Value!=source.ObjectId&&copy!=null&&copy.GetType()==source.GetType()&&copy.Database==source.Database&&copy.OwnerId==targetOwner,
            "Native mirror copy has no distinct exact root in the requested space.");
        foreach(IdPair pair in mapping)
        {
            cancel();if(!pair.Key.IsNull&&!pair.Value.IsNull)map[Id(pair.Key)!]=Id(pair.Value)!;
            if(pair.IsCloned&&pair.Value!=root.Value&&tr.GetObject(pair.Value,OpenMode.ForRead) is Entity child)
                Need(child.OwnerId!=targetOwner,"Native mirror copy produced an unreported additional top-level entity.");
        }
        Need(Exact(Remap(content,map,cancel),Content(copy,tr,cancel))&&Exact(Remap(data,map,cancel),XData(copy,cancel))&&Exact(Remap(metadata,map,cancel),Metadata(copy,tr,cancel)),
            "Native mirror copy did not retain content/format or mapped owned data.");
        foreach(string notice in Apply(copy,operation,tr,cancel))warning?.Invoke(notice);
        Need(Exact(original,CadAnnotationTransformEditor.Snapshot(source,cancel))&&Exact(data,XData(source,cancel))&&Exact(metadata,Metadata(source,tr,cancel))&&Exact(content,Content(source,tr,cancel)),
            "Native mirror copy changed its retained original.");
        return copy;
    }
}
