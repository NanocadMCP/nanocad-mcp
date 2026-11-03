using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class AnnotationReader
{
    private static double N(double n) => double.IsFinite(n) ? n : throw new DomainException("OBJECT_READ_FAILED", "Non-finite annotation geometry.");
    private static double[] P(Point3d p) => new[] { N(p.X), N(p.Y), N(p.Z) };
    private static double[] V(Vector3d v) => new[] { N(v.X), N(v.Y), N(v.Z) };
    private static void Bytes(long bytes)
    {
        if(bytes>Wire.PayloadBudget-8192)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Stored annotation detail exceeds the actual response-byte budget; no element-count truncation occurred.");
    }
    private static int StoredBytes(object value)=>System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length;
    private static string T(string value) {Bytes(System.Text.Encoding.UTF8.GetByteCount(value));return value;}
    public static object Leader(MLeader leader, EntitySummary summary, CancellationToken cancel)
    {
        // LeaderCount/LeaderLineCount are exception-only SDK stubs; enumerate
        // native indices with explicit bounds instead.
        var indices = leader.GetLeaderIndexes();
        var lines = new List<object>(); long bytes=1024;
        foreach (int index in indices)
        {
            cancel.ThrowIfCancellationRequested(); var lineIndices = leader.GetLeaderLineIndexes(index);
            foreach (int line in lineIndices)
            {
                int count = leader.VerticesCount(line);
                if(count<0)throw new DomainException("INVALID_GEOMETRY","Native leader returned a negative vertex count.");
                var points = new List<double[]>();
                for (int i = 0; i < count; i++) { cancel.ThrowIfCancellationRequested(); var point=P(leader.GetVertex(line, i));bytes+=StoredBytes(point)+1;Bytes(bytes);points.Add(point); }
                bytes+=96;Bytes(bytes);
                lines.Add(new { leader_index = index, line_index = line, vertices = points });
            }
        }
        string contentType = leader.ContentType.ToString(); string? contents = null, plain = null; double[]? location = null; EntityRef? block = null;
        if (contentType == "MTextContent")
        {
            using var text = leader.MText;
            if (text != null) { contents = T(text.Contents); plain = MTextContent.PlainText(contents); }
            location = P(leader.TextLocation);
        }
        else if (contentType == "BlockContent")
        {
            var id = leader.BlockContentId;
            if (!id.IsNull) block = new(summary.@ref.bridge_instance_id, summary.@ref.document_id, Validation.NormalizeHandle(id.Handle.ToString()));
            location = P(leader.BlockPosition);
        }
        cancel.ThrowIfCancellationRequested();
        var extended=CadMLeaderStyleWriter337.TryReadLeaderProperties(leader,summary,cancel.ThrowIfCancellationRequested);
        var editing=CadMLeaderGeometry388.TryRead(leader,cancel.ThrowIfCancellationRequested);
        var instance=CadMLeaderInstance442.TryRead(leader,cancel.ThrowIfCancellationRequested);
        object Result(object? style,string? reason)=>new { annotation_kind = "MLeader", content_type = contentType, contents, plain_text = plain, content_location = location, block_ref = block,
            normal = V(leader.Normal), line_type = leader.LeaderLineType.ToString(), lines,style,style_unavailable_reason=reason,leader_editing=editing.Value,leader_editing_unavailable_reason=editing.Reason,instance_editing=instance.Value,instance_editing_unavailable_reason=instance.Reason };
        var result=Result(extended.Value,extended.Reason);
        if(StoredBytes(result)>Wire.PayloadBudget-8192&&instance.Value!=null){instance=(null,"RESPONSE_LIMIT_EXCEEDED: Optional native callout instance state exceeds the containing response; base geometry is retained.");result=Result(extended.Value,extended.Reason);}
        if(StoredBytes(result)>Wire.PayloadBudget-8192&&extended.Value!=null)
            result=Result(null,"RESPONSE_LIMIT_EXCEEDED: Stored leader geometry fits, but additional style metadata exceeds the actual response-byte budget.");
        if(StoredBytes(result)>Wire.PayloadBudget-8192&&editing.Value!=null)
        {
            editing=(null,"RESPONSE_LIMIT_EXCEEDED: Optional complete native leader token/layout exceeds the containing response; legacy leader geometry is retained.");
            result=Result(extended.Value,extended.Reason);
            if(StoredBytes(result)>Wire.PayloadBudget-8192&&extended.Value!=null)result=Result(null,"RESPONSE_LIMIT_EXCEEDED: Additional style metadata exceeds the actual response-byte budget.");
        }
        Bytes(StoredBytes(result));return result;
    }
    public static object HatchGeometry(Hatch hatch, CancellationToken cancel,bool includeEditing=true)
    {
        int count = hatch.NumberOfLoops;
        if (count < 0) throw new DomainException("INVALID_GEOMETRY", "Native hatch returned a negative loop count.");
        var loops = new List<object>(); long bytes=1024; bool complete = true;
        for (int i = 0; i < count; i++)
        {
            cancel.ThrowIfCancellationRequested(); var loop = hatch.GetLoopAt(i);var boundary=HatchLoopGeometry.Read(loop,cancel);
            complete&=boundary.Complete;
            using var ids=hatch.GetAssociatedObjectIdsAt(i);
            var associated_handles=ids.Cast<ObjectId>().Select(id=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString())).ToArray();
            var item=new { loop_type = loop.LoopType.ToString(), boundary_kind = boundary.Kind, vertices = boundary.Vertices, associated_handles };
            bytes+=StoredBytes(item)+1;Bytes(bytes);loops.Add(item);
        }
        var editing=includeEditing?CadHatchEditingReader.TryRead(hatch,cancel.ThrowIfCancellationRequested):default;
        var fill=includeEditing?CadHatchFillReader377.TryRead(hatch,cancel.ThrowIfCancellationRequested):default;
        bool gradient=hatch.HatchObjectType==HatchObjectType.GradientObject;
        object Result(object? details,string? reason,object? fillDetails,string? fillReason)=>new { annotation_kind = "Hatch", pattern_name = gradient?null:T(hatch.PatternName), pattern_type = gradient?null:hatch.PatternType.ToString(), pattern_angle = gradient?(double?)null:N(hatch.PatternAngle),
            pattern_scale = gradient?(double?)null:N(hatch.PatternScale), pattern_double = gradient?(bool?)null:hatch.PatternDouble, associative = hatch.Associative,
            pattern_space=editing.Pattern?.Space,origin_ocs=new[]{N(hatch.Origin.X),N(hatch.Origin.Y)},hatch_style=CadHatchEditingReader.Style(hatch.HatchStyle),
            normal = V(hatch.Normal), elevation = N(hatch.Elevation), boundary_complete = complete, loops,
            editing=details,editing_unavailable_reason=reason,fill_editing=fillDetails,fill_editing_unavailable_reason=fillReason };
        var result=Result(editing.Editing,editing.Reason,fill.Value,fill.Reason);
        if(StoredBytes(result)>Wire.PayloadBudget-8192&&fill.Value!=null)
            result=Result(editing.Editing,editing.Reason,null,"RESPONSE_LIMIT_EXCEEDED: Complete active fill details exceed the actual response-byte budget; no partial colors/token are published.");
        if(StoredBytes(result)>Wire.PayloadBudget-8192&&editing.Editing!=null)
            result=Result(null,"RESPONSE_LIMIT_EXCEEDED: Stored loops fit, but additional hatch-edit metadata exceeds the actual response-byte budget.",null,"RESPONSE_LIMIT_EXCEEDED: Complete fill details exceed the actual response-byte budget.");
        Bytes(StoredBytes(result));return result;
    }
    public static object TableGeometry(Table table, CancellationToken cancel)
    {
        int rows = table.Rows.Count, columns = table.Columns.Count;
        if (rows < 0 || columns < 0)throw new DomainException("INVALID_GEOMETRY", "Native table returned negative dimensions.");
        var cells = new List<object>(); long bytes=1024;
        using var fieldTransaction=table.Database.TransactionManager.StartTransaction();
        for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++)
        {
            cancel.ThrowIfCancellationRequested(); int count = table.Cells[row,column].Contents.Count;
            if(count<0)throw new DomainException("INVALID_GEOMETRY","Native table returned a negative content count.");
            var texts = new List<string>();var observations=new List<object>();
            for (int i = 0; i < count; i++)
            {
                var kind=table.GetContentTypes(row,column,i);
                var field=kind==CellContentTypes.Block?ObjectId.Null:table.GetFieldId(row,column,i);
                var cache=field.IsNull?null:CadTableFieldCache365.Read(table,field,fieldTransaction,cancel.ThrowIfCancellationRequested);
                var text=T(cache==null?table.GetTextString(row,column,i):cache.Text);bytes+=StoredBytes(text)+1;Bytes(bytes);texts.Add(text);
                var observation=new{content_index=i,text_source=cache==null?"native_Table_stored_text":CadTableFieldCache365.Source,text_unavailable_reason=cache?.TextReason};
                bytes+=StoredBytes(observation)+1;Bytes(bytes);observations.Add(observation);
            }
            bytes+=64;Bytes(bytes);cells.Add(new { row, column, text_contents = texts,text_observations=observations });
        }
        var rowHeights=Enumerable.Range(0,rows).Select(i=>N(table.RowHeight(i))).ToArray();
        var columnWidths=Enumerable.Range(0,columns).Select(i=>N(table.ColumnWidth(i))).ToArray();
        // Complete frame observations before publishing the editing token.
        // Result construction must not call native layout getters afterward.
        var position=P(table.Position);var direction=V(table.Direction);var normal=V(table.Normal);
        double width=N(table.Width),height=N(table.Height);
        object Result(object? details,string? reason,object? indexed,string? indexedReason)=>new { annotation_kind = "Table", position, direction, normal,
            width, height, rows, columns, row_heights=rowHeights, column_widths=columnWidths, cells, content_scope = "stored_scalar_text_and_existing_native_Field_text_cache; missing_cache_explicit; no_expression_evaluation_or_data_link_refresh",
            editing=details,editing_unavailable_reason=reason,contents_editing=indexed,contents_editing_unavailable_reason=indexedReason };
        var indexed=CadTableContentsReader364.TryRead(table,cancel.ThrowIfCancellationRequested);
        var editing=CadTableEditingReader.TryRead(table,cancel.ThrowIfCancellationRequested);
        var result=Result(editing.Editing,editing.Reason,indexed.Editing,indexed.Reason);
        if(StoredBytes(result)>Wire.PayloadBudget-8192&&indexed.Editing!=null)
            result=Result(editing.Editing,editing.Reason,null,"RESPONSE_LIMIT_EXCEEDED: Stored contents plus indexed edit metadata exceed actual response-byte budget; no partial contents token is published.");
        if(StoredBytes(result)>Wire.PayloadBudget-8192&&editing.Editing!=null)
            result=Result(null,"RESPONSE_LIMIT_EXCEEDED: Stored contents fit, but additional cell-edit metadata exceeds the actual response-byte budget.",null,"RESPONSE_LIMIT_EXCEEDED: Indexed edit metadata exceeds actual response-byte budget.");
        Bytes(StoredBytes(result));return result;
    }
}
