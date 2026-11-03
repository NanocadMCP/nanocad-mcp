using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class SheetMetalHistory
{
    // HelloMultiCAD/3dSheetMetal.makeHole uses GetObject and an ISmSolid
    // interface cast; a HistoryItem base wrapper hides this native subtype.
    public static object? Read(McObjectId nodeId,McObjectId bodyId)
    {
        var item=nodeId.GetObject();
        if(item is not ISmSolid sheet)return null;
        if(item.ID!=nodeId || sheet.FeatureEnt.ID!=nodeId || sheet.Solid.ID!=bodyId)
            throw new DomainException("OBJECT_IDENTITY_MISMATCH","Sheet metal interface does not match the requested feature and body.");
        string? Id(McObjectId id)=>id.IsNull?null:id.ToString();
        return new{kind="sheet_solid",profile_id=Id(sheet.ProfileID),extrusion_type=sheet.ExtrusionType.ToString(),
            reversed_direction=sheet.IsReversedDirection,distance1_variable_id=Id(sheet.Dist1VarID),distance2_variable_id=Id(sheet.Dist2VarID),
            thickness_variable_id=Id(sheet.ThicknessVarID),radius_variable_id=Id(sheet.RadiusVarID),
            value_source="get_model_variables in the same context; variable role depends on profile and extrusion mode"};
    }
}
