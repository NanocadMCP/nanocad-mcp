using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Index=NanoCADBridge.CadTableContentsReader364;

namespace NanoCADBridge;

// The existing resize_table branch, using the actual indexed scalar adapter.
// No slot/value/style setter, indexless replacement or Table reconstruction.
internal static class CadTableIndexedSize371
{
    internal sealed record State(Index.Snapshot Contents,JsonElement Sizes,string[] RowRoles);
    static void Need(bool good,string reason,string code="UNSUPPORTED_TABLE_CONTENT",object? details=null)
    {if(!good)throw new DomainException(code,reason,false,details);}
    static bool Same(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    internal static bool UsesIndexed(Table table,Action cancel)
    {
        for(int row=0;row<table.Rows.Count;row++)for(int column=0;column<table.Columns.Count;column++)
        {cancel();if(table.Cells[row,column].Contents.Count!=1)return true;}
        return false;
    }
    internal static State Capture(Table table,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(table.GetType()==typeof(Table)&&!table.ObjectId.IsNull&&table.ObjectId.IsValid&&!table.ObjectId.IsErased&&
            !table.ObjectId.IsEffectivelyErased&&table.Database?.UnmanagedObject==db.UnmanagedObject,
            "Indexed resizing requires the exact current resident local Table.");
        Need(!table.OwnerId.IsNull&&table.OwnerId.IsValid&&!table.OwnerId.IsErased&&!table.OwnerId.IsEffectivelyErased,
            "Native Table owner is unavailable.");
        var owner=tr.GetObject(table.OwnerId,OpenMode.ForRead);
        Need(owner.GetType()==typeof(BlockTableRecord)&&owner is BlockTableRecord block&&block.OwnerId==db.BlockTableId&&
            !block.IsDependent&&!block.IsFromExternalReference&&!block.IsFromOverlayReference&&block.Database?.UnmanagedObject==db.UnmanagedObject,
            "Indexed resizing requires actual ordinary local block ownership.");
        Need(Math.Abs(table.Position.Z)<=1e-9&&table.Normal.IsEqualTo(Vector3d.ZAxis)&&Math.Abs(table.Direction.Z)<=1e-9,
            "Table sizing retains its existing native WCS XY/Z0 scope.","OUT_OF_SCOPE");
        Need(!table.HasFields,"Table sizing retains its non-Field scalar scope.");
        using(var reactors=table.GetPersistentReactorIds())Need(reactors.Count==0,"Table sizing does not edit persistent root dependencies.");
        // Test discriminators and binding/protection before indexed raw values.
        for(int row=0;row<table.Rows.Count;row++)for(int column=0;column<table.Columns.Count;column++)
        {
            cancel();var cell=table.Cells[row,column];
            Need(cell.IsMerged==false&&!table.IsLinked(row,column)&&table.IsContentEditable(row,column)&&table.IsFormatEditable(row,column),
                "Table sizing requires unmerged, unlinked and content/format-editable cells.");
            for(int index=0;index<cell.Contents.Count;index++)
            {
                cancel();Need(table.GetContentTypes(row,column,index)==CellContentTypes.Value&&table.GetFieldId(row,column,index).IsNull&&
                    !cell.Contents[index].HasFormula&&string.IsNullOrEmpty(table.GetFormula(row,column,index)),
                    "Table sizing supports ordinary indexed scalar slots; Field/block/formula bindings require their sizing adapter.");
            }
        }
        var contents=Index.Capture(table,tr,cancel);
        Need(contents.Contents.SelectMany(slots=>slots).All(Index.Ordinary),"Indexed resizing requires actual ordinary scalar storage/types/units.");
        var roles=new string[contents.Rows];
        for(int row=0;row<roles.Length;row++){cancel();roles[row]=table.RowType(row).ToString();}
        double width=table.Width,height=table.Height;
        Need(double.IsFinite(width)&&double.IsFinite(height)&&width>0&&height>0&&width<=1e9&&height<=1e9,
            "Native Table physical sizes are unavailable or invalid.","INVALID_GEOMETRY");
        return new(contents,Wire.Element(new{rows=contents.Rows,columns=contents.Columns,width,height,
            row_heights=contents.RowHeights,column_widths=contents.ColumnWidths}),roles);
    }
    internal static void Preflight(Table table,IEnumerable<ResizeTableSpec> edits,Database db,Transaction tr,Action cancel)
    {
        var sizes=Capture(table,db,tr,cancel).Sizes;
        foreach(var edit in edits){cancel();sizes=TableSizeVerification.Expected(sizes,edit);}
    }
    static object Stored(Index.Snapshot value,Index.Slot[][] contents)=>new{identity=value.Identity,common=value.Common,
        rows=value.Rows,columns=value.Columns,cells=value.Cells,native_contents=contents,cell_overrides=value.CellOverrides};
    static string[] Verify(State before,State after,JsonElement expectedSizes,Action cancel)
    {
        cancel();TableSizeVerification.Verify(expectedSizes,after.Sizes);
        Need(Same(before.RowRoles,after.RowRoles)&&before.Contents.Rows==after.Contents.Rows&&before.Contents.Columns==after.Contents.Columns&&
            before.Contents.Contents.Length==after.Contents.Contents.Length,"Native resizing changed indexed row roles or topology.","NATIVE_TABLE_MISMATCH");
        var expected=new Index.Slot[before.Contents.Contents.Length][];bool autoScaleChanged=false;
        for(int cell=0;cell<expected.Length;cell++)
        {
            cancel();var old=before.Contents.Contents[cell];var current=after.Contents.Contents[cell];
            Need(old.Length==current.Length,"Native resizing changed indexed slot count/order.","NATIVE_TABLE_MISMATCH");
            expected[cell]=new Index.Slot[old.Length];
            for(int index=0;index<old.Length;index++)
            {
                cancel();expected[cell][index]=old[index];
                // Same actual automatic-scale distinction as the indexed style
                // adapter. Auto mode/masks and explicit scale stay exact; only
                // the generated scale of an unoverridden auto slot may reflow.
                if(old[index].auto_scale&&current[index].auto_scale&&(old[index].overrides&8)==0)
                {autoScaleChanged|=old[index].scale!=current[index].scale;expected[cell][index]=old[index] with{scale=current[index].scale};}
            }
        }
        var prior=Stored(before.Contents,expected);var actual=Stored(after.Contents,after.Contents.Contents);
        if(!Same(prior,actual))throw new DomainException("NATIVE_TABLE_MISMATCH","Native resizing changed retained scalar value/type/unit/bits/order/format/overrides, cell layout, style or placement.",false,
            CadTableStyleCloneDiagnostics367.Capture("resize_table",before.Contents.Identity.Split(':')[0],Wire.Element(prior),Wire.Element(actual),cancel,
                "resident_Table_resize_retained_indexed_scalars","retained_raw_indexed_scalar_state","actual_native_indexed_scalar_state"));
        var origins=before.Contents.Contents.Select(slots=>Enumerable.Range(0,slots.Length).ToArray()).ToArray();
        CadTableContentsMetadata364.Verify(before.Contents,after.Contents,origins,cancel);
        return autoScaleChanged?new[]{"TABLE_NATIVE_AUTO_SCALE_REFLOWED: Native resizing reflowed generated scale of an unoverridden automatic scalar slot; raw storage, auto mode and explicit formatting are retained."}:Array.Empty<string>();
    }
    internal static string[] Apply(Table table,ResizeTableSpec edit,Database db,Transaction tr,Action cancel)
    {
        var before=Capture(table,db,tr,cancel);var expected=TableSizeVerification.Expected(before.Sizes,edit);
        foreach(var row in edit.Rows){cancel();table.SetRowHeight(row.Index,row.Size);}
        foreach(var column in edit.Columns){cancel();table.SetColumnWidth(column.Index,column.Size);}
        cancel();table.GenerateLayout();cancel();table.RecomputeTableBlock(true);cancel();
        return Verify(before,Capture(table,db,tr,cancel),expected,cancel);
    }
}
