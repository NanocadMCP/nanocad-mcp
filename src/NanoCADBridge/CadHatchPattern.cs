using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadHatchPattern
{
    internal static HatchPatternLine Definition(Hatch hatch)
    {
        if(hatch.NumberOfPatternDefinitions!=1)throw new DomainException("HATCH_PATTERN_UNSUPPORTED","ANSI31 must resolve to exactly one continuous line definition.");
        var line=hatch.GetPatternDefinitionAt(0);
        // DoubleCollection is a managed array container, not a disposable SDK wrapper.
        return new(line.Angle,line.BaseX,line.BaseY,line.OffsetX,line.OffsetY,line.GetDashes().Count);
    }
    internal static double Configure(Hatch hatch,HatchSpec spec)
    {
        hatch.SetHatchPattern(HatchPatternType.PreDefined,spec.PatternName);
        if(spec.PatternName=="SOLID")return 0;
        hatch.PatternDouble=false;hatch.PatternScale=spec.PatternScale;hatch.PatternAngle=spec.PatternAngle;
        var configured=Definition(hatch);
        double configuredBound=HatchPatternDensity.Bound(spec.Boundary,configured,spec.Holes?.Sum(h=>h.Vertices?.Length??2)??0);
        if(hatch.PatternName!="ANSI31"||hatch.PatternType!=HatchPatternType.PreDefined||hatch.PatternDouble||
            Math.Abs(hatch.PatternAngle-spec.PatternAngle)>1e-10||Math.Abs(hatch.PatternScale-spec.PatternScale)>1e-10)
            throw new DomainException("NATIVE_HATCH_MISMATCH","Native hatch did not retain requested pattern settings.");
        return configuredBound;
    }
    internal static void VerifyEvaluation(Hatch hatch,double bound)
    {
        if(bound==0)return;
        int count=hatch.NumberOfHatchLines;
        if(count<1)throw new DomainException("NATIVE_HATCH_MISMATCH","Native patterned hatch generated no lines.",false,new{generated_lines=count,estimated_upper_bound=bound});
    }
}
