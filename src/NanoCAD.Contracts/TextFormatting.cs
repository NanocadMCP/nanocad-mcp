namespace NanoCAD.Contracts;

public sealed record TextFormatting(EntityRef text_style_ref,double oblique,double thickness,bool mirrored_in_x,bool mirrored_in_y,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] TextEditing442? editing=null);
