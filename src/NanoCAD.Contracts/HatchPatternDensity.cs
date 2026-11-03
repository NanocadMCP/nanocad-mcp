namespace NanoCAD.Contracts;

// Value-only snapshot; no native SDK objects cross the contract boundary.
public sealed record HatchPatternLine(double Angle,double BaseX,double BaseY,double OffsetX,double OffsetY,int DashCount);
public static class HatchPatternDensity
{
    public static double Bound(Point2[] polygon,HatchPatternLine line,int additionalEdges=0)
    {
        var values=new[]{line.Angle,line.BaseX,line.BaseY,line.OffsetX,line.OffsetY};
        if(values.Any(v=>!double.IsFinite(v))||line.DashCount!=0||polygon.Length<3||polygon.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)))
            throw new DomainException("HATCH_PATTERN_UNSUPPORTED","Expected one finite continuous pattern line and a bounded scale.");
        // GetPatternDefinitionAt returns the configured line direction and offset:
        // angle and scale are already applied. Spacing is the perpendicular
        // projection of that offset, not OffsetY or a second rotation/scale.
        double spacing=Math.Abs(-Math.Sin(line.Angle)*line.OffsetX+Math.Cos(line.Angle)*line.OffsetY);
        if(!double.IsFinite(spacing)||spacing<1e-8)throw new DomainException("HATCH_PATTERN_UNSUPPORTED","Pattern spacing cannot be bounded safely for this angle.");
        double dx=polygon.Max(p=>p.X)-polygon.Min(p=>p.X),dy=polygon.Max(p=>p.Y)-polygon.Min(p=>p.Y);
        double diagonal=Math.Sqrt(dx*dx+dy*dy);
        // At most N boundary edges can contribute separate portions of one line,
        // even at vertices or collinear edges; add three lines for endpoint cases.
        if(additionalEdges<0)throw new DomainException("HATCH_PATTERN_UNSUPPORTED","Invalid inner-loop edge count.");
        double upper=(Math.Ceiling(diagonal/spacing)+3)*(polygon.Length+additionalEdges);
        if(!double.IsFinite(upper))throw new DomainException("INVALID_GEOMETRY","Pattern density estimate is nonfinite.");
        return upper;
    }
}
