namespace NanoCAD.Contracts;

public sealed record MTextFormatting(EntityRef text_style_ref,string line_spacing_style,double line_spacing_factor,
    string flow_direction,string column_type,MTextBackground background,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] MTextEditingMetadata? editing=null);
public sealed record MTextBackground(bool enabled,bool use_drawing_background,double scale_factor,object color);
