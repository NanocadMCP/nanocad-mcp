using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadExtendedDimensions356
{
    internal static bool Supported(Type type)=>type==typeof(ArcDimension)||type==typeof(RadialDimensionLarge);
    private static Point3d P(Point2 p)=>new(p.X,p.Y,0);
    internal static Dimension Create(EntitySpec spec,ObjectId style)=>spec switch{
        ArcLengthDimensionSpec356 a=>new ArcDimension(P(a.Center),P(a.First),P(a.Second),P(a.ArcPoint),a.TextOverride,style),
        JoggedRadiusDimensionSpec356 j=>new RadialDimensionLarge(P(j.Center),P(j.ChordPoint),P(j.OverrideCenter),P(j.JogPoint),j.JogAngle,j.TextOverride,style),
        _=>throw new DomainException("INVALID_ARGUMENT","Unknown extended native dimension.")};
    private static double Number(double n)=>double.IsFinite(n)?n:throw new DomainException("OBJECT_READ_FAILED","Extended dimension stores nonfinite geometry.");
    private static double[] Point(Point3d p)=>new[]{Number(p.X),Number(p.Y),Number(p.Z)};
    internal static object Definition(Dimension d)=>d switch{
        ArcDimension a=>new{center_point=Point(a.CenterPoint),xline1_point=Point(a.XLine1Point),xline2_point=Point(a.XLine2Point),arc_point=Point(a.ArcPoint)},
        RadialDimensionLarge j=>new{center=Point(j.Center),chord_point=Point(j.ChordPoint),override_center=Point(j.OverrideCenter),jog_point=Point(j.JogPoint),jog_angle=Number(j.JogAngle)},
        _=>throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT","Unsupported native extended dimension.")};
    internal static object? Details(Dimension d)=>d is ArcDimension a?new{is_partial=a.IsPartial,arc_start_parameter=a.IsPartial?(double?)Number(a.ArcStartParam):null,arc_end_parameter=a.IsPartial?(double?)Number(a.ArcEndParam):null,
        arc_symbol_type=a.ArcSymbolType,has_leader=a.HasLeader,leader1_point=a.HasLeader?Point(a.Leader1Point):null,leader2_point=a.HasLeader?Point(a.Leader2Point):null,
        scope="stored_native_arc_options; no implicit parameter_conversion"}:null;
    internal static JsonElement DetailSnapshot(Dimension dimension)=>JsonSerializer.SerializeToElement(Details(dimension),Wire.Json);
    internal static JsonElement PlacementControls(Dimension dimension)=>dimension switch{
        ArcDimension arc=>Wire.Element(new{is_partial=arc.IsPartial,has_leader=arc.HasLeader,arc_symbol_type=arc.ArcSymbolType,
            scope="native_mode_controls_retained; partial_parameters_observed_in_snapshots_not_certified_as_transformed_physical_span"}),
        RadialDimensionLarge jog=>Wire.Element(new{jog_angle=Number(jog.JogAngle)}),
        _=>JsonSerializer.SerializeToElement((object?)null,Wire.Json)};
    internal static JsonElement Controls(Dimension dimension,bool includeSymbol=true)
    {
        var details=DetailSnapshot(dimension);if(details.ValueKind!=JsonValueKind.Object)return details;
        return Wire.Element(details.EnumerateObject().Where(field=>field.Name is not ("leader1_point" or "leader2_point")&&(includeSymbol||field.Name!="arc_symbol_type")).ToDictionary(field=>field.Name,field=>field.Value.Clone()));
    }
    internal static void SetDefinition(Dimension d,JsonElement value,Action cancel)
    {
        Point3d Get(string name){cancel();var v=value.GetProperty(name);return new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());}
        if(d is ArcDimension a){a.CenterPoint=Get("center_point");a.XLine1Point=Get("xline1_point");a.XLine2Point=Get("xline2_point");a.ArcPoint=Get("arc_point");}
        else{var j=(RadialDimensionLarge)d;j.Center=Get("center");j.ChordPoint=Get("chord_point");j.OverrideCenter=Get("override_center");j.JogPoint=Get("jog_point");j.JogAngle=value.GetProperty("jog_angle").GetDouble();}
        cancel();
    }
    internal static void VerifyCreated(Dimension d,EntitySpec spec,ObjectId style,Action cancel)
    {
        cancel();var content=(IDimensionCreation)spec;
        if(d.DimensionStyle!=style||d.DimensionText!=content.TextOverride||d.Normal!=Vector3d.ZAxis||d.Elevation!=0||d.HasFields)
            throw new DomainException("NATIVE_DIMENSION_MISMATCH","Extended dimension lost requested native type/style/text/plane.");
        using var reactors=d.GetPersistentReactorIds();if(reactors.Count!=0)throw new DomainException("NATIVE_DIMENSION_MISMATCH","New independent dimension gained unrequested dependencies.");
        object requested=spec is ArcLengthDimensionSpec356 a?new{center_point=new[]{a.Center.X,a.Center.Y,0.0},xline1_point=new[]{a.First.X,a.First.Y,0.0},xline2_point=new[]{a.Second.X,a.Second.Y,0.0},arc_point=new[]{a.ArcPoint.X,a.ArcPoint.Y,0.0}}:
            (object)new{center=new[]{((JoggedRadiusDimensionSpec356)spec).Center.X,((JoggedRadiusDimensionSpec356)spec).Center.Y,0.0},chord_point=new[]{((JoggedRadiusDimensionSpec356)spec).ChordPoint.X,((JoggedRadiusDimensionSpec356)spec).ChordPoint.Y,0.0},
                override_center=new[]{((JoggedRadiusDimensionSpec356)spec).OverrideCenter.X,((JoggedRadiusDimensionSpec356)spec).OverrideCenter.Y,0.0},jog_point=new[]{((JoggedRadiusDimensionSpec356)spec).JogPoint.X,((JoggedRadiusDimensionSpec356)spec).JogPoint.Y,0.0},jog_angle=((JoggedRadiusDimensionSpec356)spec).JogAngle};
        if(!DimensionEditingValidation341.SameDefinition(Wire.Element(requested),Wire.Element(Definition(d))))throw new DomainException("NATIVE_DIMENSION_MISMATCH","Native extended dimension changed requested references or physical placement.");
        cancel();
    }
}
