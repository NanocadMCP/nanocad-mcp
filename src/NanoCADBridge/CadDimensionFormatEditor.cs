using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadDimensionFormatEditor
{
    internal static void CheckSource(Entity source)
    {
        using var nativeBinding=CadNativeDimensionBinding357.Bind(source);source=nativeBinding.Value;
        if(source is not Dimension d || !DimensionGeometryReader.Supported(d.GetType()) || d.HasFields)
            throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT", "Format edits require an ordinary native dimension without fields.");
        if(d.DimensionStyle.IsNull || !d.DimensionStyle.IsValid || d.DimensionStyle.IsErased)
            throw new DomainException("DIMENSION_STYLE_UNAVAILABLE", "Existing dimension style is unavailable.");
    }
    private static object MeasuredDefinition(Dimension d) => Wire.Element(DimensionGeometryReader.Definition(d)).EnumerateObject()
        .Where(p => p.Name is not ("dimline_point" or "arc_point" or "leader_end_point" or "leader_length"))
        .ToDictionary(p => p.Name, p => p.Value.Clone());
    internal static (object? Format,string? Reason) TryRead(Dimension d)
    {
        try { return (Read(d),null); }
        catch(System.Exception e) { return (null,"Effective dimension format is unavailable: "+e.Message); }
    }
    internal static object Read(Dimension d) => new {
        text_height=d.Dimtxt,arrow_size=d.Dimasz,dimension_scale=d.Dimscale,linear_precision=d.Dimdec,angular_precision=d.Dimadec,
        decimal_separator=d.Dimdsep.ToString(),tolerance_mode=d.Dimlim?"limits":d.Dimtol?"deviation":"none",
        upper_deviation=d.Dimtp,lower_deviation=-d.Dimtm,tolerance_precision=d.Dimtdec,
        deviation_units="native_DIMTP_signed_DIMTM_no_conversion",
        linear_factor=d.Dimlfac,linear_unit_format=d.Dimlunit,angular_unit_format=d.Dimaunit,
        text_style_handle=FontCoverage.DimensionStyle(d).Handle.ToString()
    };
    internal static void Resolve(Entity source,DimensionFormatSpec edit,Database db,Transaction tr)
    {
        using var nativeBinding=CadNativeDimensionBinding357.Bind(source);source=nativeBinding.Value;
        CheckSource(source);
        if(edit.Properties.DimensionStyle is {} name) {
            var styles=(DimStyleTable)tr.GetObject(db.DimStyleTableId,OpenMode.ForRead);
            if(!styles.Has(name) || ((DimStyleTableRecord)tr.GetObject(styles[name],OpenMode.ForRead)).IsDependent)
                throw new DomainException("DIMENSION_STYLE_UNAVAILABLE", "Assign an existing local dimension style read from get_structure.");
        }
    }
    internal static void Apply(Entity source,DimensionFormatSpec edit,Database db,Transaction tr,Action cancel,bool regenerate)
    {
        using var nativeBinding=CadNativeDimensionBinding357.Bind(source);source=nativeBinding.Value;
        cancel();Resolve(source,edit,db,tr); var d=(Dimension)source;var p=edit.Properties;
        var controls=CadExtendedDimensions356.Controls(d,edit.Properties.DimensionStyle==null);
        var measured=Wire.Element(MeasuredDefinition(d));var normal=d.Normal;var elevation=d.Elevation;var text=d.DimensionText;var movement=d.Dimtmove;
        if(p.DimensionStyle is {} name)d.DimensionStyle=((DimStyleTable)tr.GetObject(db.DimStyleTableId,OpenMode.ForRead))[name];
        if(p.TextHeight is {} height)d.Dimtxt=height;
        if(p.ArrowSize is {} arrow)d.Dimasz=arrow;
        if(p.DimensionScale is {} scale)d.Dimscale=scale;
        if(p.LinearPrecision is {} linear)d.Dimdec=linear;
        if(p.AngularPrecision is {} angular)d.Dimadec=angular;
        if(p.DecimalSeparator is {} separator)d.Dimdsep=separator[0];
        if(p.ToleranceMode is {} mode){d.Dimtol=mode=="deviation";d.Dimlim=mode=="limits";}
        if(p.UpperDeviation is {} upper)d.Dimtp=upper;
        if(p.LowerDeviation is {} lower)d.Dimtm=-lower;
        if(p.TolerancePrecision is {} precision)d.Dimtdec=precision;
        // Preserve the existing movement policy even when a style supplies another default.
        if(d.Dimtmove!=movement)d.Dimtmove=movement;
        FontCoverage.RequireEntity(tr,d);
        if(d.Dimtol)FontCoverage.Require(tr,FontCoverage.DimensionStyle(d),"±");
        if(regenerate){cancel();d.RecomputeDimensionBlock(true);}
        bool Near(double? wanted,double actual)=>!wanted.HasValue || wanted.Value==actual;
        bool Same(int? wanted,int actual)=>!wanted.HasValue || wanted.Value==actual;
        if(!Near(p.TextHeight,d.Dimtxt)||!Near(p.ArrowSize,d.Dimasz)||!Near(p.DimensionScale,d.Dimscale)||
           !Same(p.LinearPrecision,d.Dimdec)||!Same(p.AngularPrecision,d.Dimadec)||!Same(p.TolerancePrecision,d.Dimtdec)||
           !Near(p.UpperDeviation,d.Dimtp)||!Near(p.LowerDeviation,-d.Dimtm)||p.DecimalSeparator!=null&&d.Dimdsep!=p.DecimalSeparator[0]||
           p.ToleranceMode!=null&&(d.Dimlim?"limits":d.Dimtol?"deviation":"none")!=p.ToleranceMode||
           p.DimensionStyle!=null&&d.DimensionStyle!=((DimStyleTable)tr.GetObject(db.DimStyleTableId,OpenMode.ForRead))[p.DimensionStyle])
            throw new DomainException("NATIVE_FORMAT_MISMATCH","Native dimension format did not retain the requested settings.");
        if(!ModelParameterSnapshot.Equivalent(controls,CadExtendedDimensions356.Controls(d,edit.Properties.DimensionStyle==null)))throw new DomainException("NATIVE_FORMAT_MISMATCH","Native format changed retained explicit arc partial/leader controls.");
        if(!ModelParameterSnapshot.Equivalent(measured,Wire.Element(MeasuredDefinition(d))) || d.Normal!=normal || d.Elevation!=elevation || d.DimensionText!=text || d.Dimtmove!=movement)
            throw new DomainException("NATIVE_FORMAT_MISMATCH","Dimension format changed measured references or an unrequested text/movement policy.");
    }
}
