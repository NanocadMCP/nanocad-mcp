using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadTableSizeEditor
{
    internal static CadTableSizeState361.State Capture(Table table,Action cancel)
    {
        CheckSource(table,cancel);
        // Numeric cells are observed through the existing native scalar reader.
        // Resize retains the narrower unmerged, non-Field content scope; it
        // never converts a numeric value to text or relaxes literal text edits.
        var contents=CadTableEditingReader.Capture(table,cancel,allowNumeric:true);
        cancel();double width=table.Width,height=table.Height;
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0||width>1e9||height>1e9)
            throw new DomainException("INVALID_GEOMETRY","Native table physical size is unavailable or invalid.");
        return new(contents,Wire.Element(new{rows=contents.Rows,columns=contents.Columns,width,height,
            row_heights=contents.RowHeights,column_widths=contents.ColumnWidths}));
    }
    internal static void CheckSource(Table table,Action cancel)
    {
        cancel();
        using var reactors=table.GetPersistentReactorIds();
        if(table.HasFields || reactors.Count!=0)
            throw new DomainException("UNSUPPORTED_TABLE_CONTENT","Table resizing does not support fields or persistent dependencies.");
        for(int row=0;row<table.Rows.Count;row++)for(int col=0;col<table.Columns.Count;col++)
        {
            cancel();
            if(!table.IsFormatEditable(row,col))throw new DomainException("UNSUPPORTED_TABLE_CONTENT","Table contains locked formatting.");
            var cell=table.Cells[row,col];
            if(cell.IsMerged!=false||table.IsLinked(row,col)||!table.IsContentEditable(row,col))
                throw new DomainException("UNSUPPORTED_TABLE_CONTENT","Table resizing requires unmerged, unlinked and editable cells.");
            if(cell.Contents.Count!=1||table.GetContentTypes(row,col,0)!=CellContentTypes.Value||
                !table.GetFieldId(row,col,0).IsNull||cell.Contents[0].HasFormula||!string.IsNullOrEmpty(table.GetFormula(row,col,0)))
                throw new DomainException("UNSUPPORTED_TABLE_CONTENT","Table resizing requires one native scalar per cell without Fields or formulas.");
        }
    }
    internal static void Apply(Table table,ResizeTableSpec edit,Action cancel,bool regenerate)
    {
        var before=Capture(table,cancel);
        var expected=TableSizeVerification.Expected(before.Sizes,edit);
        foreach(var row in edit.Rows){cancel();table.SetRowHeight(row.Index,row.Size);}
        foreach(var col in edit.Columns){cancel();table.SetColumnWidth(col.Index,col.Size);}
        if(regenerate){cancel();table.GenerateLayout();cancel();table.RecomputeTableBlock(true);}
        CadTableSizeState361.Verify(before,Capture(table,cancel),expected,cancel);
    }
}
