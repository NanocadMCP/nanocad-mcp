using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;
namespace NanoCAD.Contracts;

public static class TableContentFormat365
{
    // Cell appearance is separate from indexed scalar font/height/color.
    public static TableCellFormatChanges Parse(JsonElement value,string path)
    {
        Shape(value,path,"alignment","background","grids");
        if(!value.EnumerateObject().Any())throw Invalid(path,"Select a native cell appearance property.");
        string? alignment=null;
        if(value.TryGetProperty("alignment",out _))
        {alignment=Text(value,"alignment",path,32);if(!TableEditingValidation.Alignments.Contains(alignment,StringComparer.Ordinal))throw Invalid(path+".alignment","Select native cell alignment.");}
        return new(alignment,null,null,null,Background:value.TryGetProperty("background",out var background)?TableStyleRoleDefaults365.Background(background,path+".background"):null,
            Grids:value.TryGetProperty("grids",out var grids)?TableStyleRoleDefaults365.Grids(grids,path+".grids"):null);
    }
}
