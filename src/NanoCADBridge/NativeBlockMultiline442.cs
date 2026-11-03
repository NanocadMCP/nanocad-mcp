using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// SDK database.h:8014-8022, AttributeReference.MTextAttribute and inherited
// Entity.TransformBy. The embedded MText is borrowed storage, not an owned
// detached entity. Placement never edits its Contents or calls UpdateMTextAttribute
// on an AttributeReference clone. Native occurrence placement owns that update.
internal static class NativeBlockMultiline442
{
    private static void Need([DoesNotReturnIf(false)] bool value,string message,string code="UNSUPPORTED_BLOCK_MULTILINE")
    {if(!value)throw new DomainException(code,message);}
    private static double Number(double value)
    {Need(double.IsFinite(value),"Native multiline geometry contains a nonfinite value.","INVALID_GEOMETRY");return value;}
    private static double[] Point(Point3d value)
    {
        Need(Math.Abs(Number(value.Z))<=1e-9,"Native block multiline text must remain in its actual WCS XY plane.");
        BlockMoveGeometry.Point(new(value.X,value.Y));return [value.X,value.Y,value.Z];
    }
    private static double[] Vector(Vector3d value)=>[Number(value.X),Number(value.Y),Number(value.Z)];
    private static string? Id(ObjectId value)=>value.IsNull?null:value.Handle.ToString();
    private static object Color(Teigha.Colors.Color value)=>new{method=value.ColorMethod.ToString(),aci=value.IsByAci?(int?)value.ColorIndex:null,
        rgb=value.IsByColor?new[]{(int)value.Red,(int)value.Green,(int)value.Blue}:null};
    private static JsonElement Embedded(MText text,Action cancel)
    {
        cancel();Need(text.GetType()==typeof(MText)&&text.UnmanagedObject!=IntPtr.Zero&&text.ObjectId.IsNull,
            "Multiline attribute must expose its actual embedded native MText definition.");
        Need(!text.HasFields&&text.Annotative!=AnnotativeStates.True&&text.ExtensionDictionary.IsNull,
            "Field-bearing, annotative or owned-extension multiline definitions require their owning adapter.");
        using var links=text.GetPersistentReactorIds();Need(links.Count==0,"Embedded multiline persistent dependencies need their owning adapter.");
        Need(Math.Abs(Number(text.Normal.X))<=1e-9&&Math.Abs(Number(text.Normal.Y))<=1e-9&&Math.Abs(Math.Abs(Number(text.Normal.Z))-1)<=1e-9&&
            Math.Abs(Number(text.Direction.Z))<=1e-9&&text.Direction.Length>0,"Native multiline text requires a finite planar frame; reflected normals are retained.");
        Need(Number(text.TextHeight)>0&&Number(text.Width)>=0,"Native multiline width/height must be finite and nonnegative.");
        Need(Enum.IsDefined(typeof(ColumnType),text.ColumnType),"Native multiline column kind is unavailable.");
        var budget=new ModelInventoryBudget(cancel);budget.Add(text.Contents);
        bool staticColumns=text.ColumnType==ColumnType.StaticColumns;var heights=new List<double>();
        if(staticColumns)for(int i=0;i<text.ColumnCount;i++){cancel();double height=Number(text.GetColumnHeight(i));budget.Add(height);heights.Add(height);}
        using var background=text.BackgroundFillColor;using var xdata=text.XData;
        var transparency=text.BackgroundTransparency;
        var value=Wire.Element(new{contents=text.Contents,position=Point(text.Location),normal=Vector(text.Normal),direction=Vector(text.Direction),
            width=text.Width,text_height=text.TextHeight,attachment=text.Attachment.ToString(),text_style=Id(text.TextStyleId),
            line_spacing_style=text.LineSpacingStyle.ToString(),line_spacing_factor=Number(text.LineSpacingFactor),flow_direction=text.FlowDirection.ToString(),
            background_fill=text.BackgroundFill,use_background_color=text.UseBackgroundColor,background_scale_factor=Number(text.BackgroundScaleFactor),background_color=Color(background),
            background_transparency=new{alpha=transparency.Alpha,by_alpha=transparency.IsByAlpha,by_layer=transparency.IsByLayer,by_block=transparency.IsByBlock},
            column_type=text.ColumnType.ToString(),column_width=Number(text.ColumnWidth),column_gutter_width=Number(text.ColumnGutterWidth),
            column_auto_height=text.ColumnAutoHeight,column_flow_reversed=text.ColumnFlowReversed,
            explicit_column_count=staticColumns?(int?)text.ColumnCount:null,explicit_column_heights=heights,
            dynamic_fixed_height=text.ColumnType==ColumnType.DynamicColumns&&!text.ColumnAutoHeight?(double?)Number(text.Height):null,
            properties=CadBlockMover.PreservedProperties(text),xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,budget.Add)});
        budget.Add(value);return value;
    }
    internal static JsonElement? Capture(DBText source,Action cancel)
    {
        if(!NativeAttributeMText377.IsMultiline(source))return null;
        var text=NativeAttributeMText377.Acquire(source);
        try{Need(text!=null,"Native multiline attribute definition is unavailable.");return Embedded(text,cancel);}
        finally{NativeAttributeMText377.Release(text);GC.KeepAlive(source);}
    }
    internal static void Check(DBText source,Database db,Transaction tr,Action cancel)
    {
        if(!NativeAttributeMText377.IsMultiline(source))return;
        _=Capture(source,cancel);var text=NativeAttributeMText377.Acquire(source);
        try
        {
            Need(text!=null&&!text.TextStyleId.IsNull&&text.TextStyleId.IsValid&&!text.TextStyleId.IsErased&&
                tr.GetObject(text.TextStyleId,OpenMode.ForRead) is TextStyleTableRecord style&&style.GetType()==typeof(TextStyleTableRecord)&&
                style.OwnerId==db.TextStyleTableId&&!style.IsDependent&&!style.IsShapeFile&&!style.IsVertical,
                "Native multiline attributes require an actual local horizontal non-shape text style.");
        }
        finally{NativeAttributeMText377.Release(text);GC.KeepAlive(source);}
    }
    internal static void CheckCopy(AttributeReference source,Action cancel)
    {
        if(!source.IsMTextAttribute)return;
        _=Capture(source,cancel);var text=NativeAttributeMText377.Acquire(source);
        try
        {
            Need(text!=null,"Native multiline definition is unavailable.");using var xdata=text.XData;
            Need(xdata==null,"Copying embedded multiline XData requires a proven owned-data clone route; no data is discarded.");
        }
        finally{NativeAttributeMText377.Release(text);GC.KeepAlive(source);}
    }
    private static T OwnedClone<T>(T source) where T:DBObject
    {
        var candidate=source.Clone();var copy=candidate as T;
        if(copy==null||copy.GetType()!=source.GetType()||!copy.AutoDelete||copy.UnmanagedObject==IntPtr.Zero||
            copy.UnmanagedObject==source.UnmanagedObject||!copy.ObjectId.IsNull||!copy.OwnerId.IsNull)
        {
            if(candidate is Teigha.Runtime.DisposableWrapper rejected&&!ReferenceEquals(rejected,source))GC.SuppressFinalize(rejected);
            throw new DomainException("UNSAFE_NATIVE_CLONE","Native multiline clone ownership/identity is unproved; it was not adopted or disposed.");
        }
        return copy;
    }
    internal static DBText Detached(DBText source,Action cancel)
    {
        var before=Capture(source,cancel);var copy=OwnedClone(source);bool safeToDispose=false;
        try
        {
            if(before.HasValue)
            {
                MText? original=null;MText? embedded=null;
                try
                {
                    original=NativeAttributeMText377.Acquire(source);embedded=NativeAttributeMText377.Acquire(copy);
                    if(original==null||embedded==null||original.UnmanagedObject==IntPtr.Zero||embedded.UnmanagedObject==IntPtr.Zero||
                        original.UnmanagedObject==embedded.UnmanagedObject)
                    {
                        throw new DomainException("UNSAFE_NATIVE_CLONE","Native attribute clone multiline storage is unproved; its root was not adopted or disposed.");
                    }
                    safeToDispose=true;
                }
                finally
                {
                    try{NativeAttributeMText377.Release(embedded);}
                    finally
                    {
                        try{NativeAttributeMText377.Release(original);}
                        finally{GC.KeepAlive(source);}
                    }
                }
                Same(before,Capture(copy,cancel));Same(before,Capture(source,cancel));
            }
            return copy;
        }
        catch
        {
            // Until both live embedded pointers are proved distinct, disposing
            // the root could delete retained source text. Quarantine it instead.
            if(safeToDispose)copy.Dispose();else GC.SuppressFinalize(copy);throw;
        }
    }
    internal static JsonElement? Expected(DBText source,Matrix3d matrix,Action cancel)
    {
        if(!NativeAttributeMText377.IsMultiline(source))return null;
        var original=NativeAttributeMText377.Acquire(source);MText? copy=null;
        try
        {
            Need(original!=null,"Native multiline definition is unavailable.");var before=Embedded(original,cancel);copy=OwnedClone(original);
            Same(before,Embedded(copy,cancel));
            // Only this separately owned MText is transformed for the expected
            // stored frame/size. Derived glyph extents/reflow are not an oracle.
            NativeAttributeMText377.Release(original);original=null;
            cancel();copy.TransformBy(matrix);cancel();return Embedded(copy,cancel);
        }
        finally{NativeAttributeMText377.Release(original);copy?.Dispose();GC.KeepAlive(source);}
    }
    internal static void Same(JsonElement? expected,JsonElement? actual)
    {
        Need(expected.HasValue==actual.HasValue&&(!expected.HasValue||BlockMoveGeometry.SameProperties(expected.Value,actual!.Value)),
            "Native block placement changed the full multiline Contents, stored frame/size, formatting, columns, background or display data.","BLOCK_MULTILINE_MISMATCH");
    }
    internal static JsonElement RootData(AttributeReference source,Transaction tr,Action cancel)
    {
        using var links=source.GetPersistentReactorIds();var ids=new List<string?>();var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId id in links){cancel();ids.Add(Id(id));budget.Add(ids[^1]);}
        var result=Wire.Element(new{handle=Id(source.ObjectId),owner=Id(source.OwnerId),extension=Id(source.ExtensionDictionary),
            reactors=ids.OrderBy(value=>value,StringComparer.Ordinal).ToArray(),owned=NativeOwnedMetadata.Capture(source,tr,cancel)});
        budget.Add(result);return result;
    }
    internal static object? RootXData(AttributeReference source,Action cancel)
    {
        if(!source.IsMTextAttribute)return null;
        using var data=source.XData;var budget=new ModelInventoryBudget(cancel);
        return data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add);
    }
}
