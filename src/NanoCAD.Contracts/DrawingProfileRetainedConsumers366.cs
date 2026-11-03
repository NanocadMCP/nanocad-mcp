using System.Text.Json;

namespace NanoCAD.Contracts;

// Current native bindings returned by an existing source-dimension edit. These
// are observed consumers of one part, not new forming features or reusable IDs.
public sealed record DrawingRetainedSweepConsumer366(string source_id,string source_handle,string projection_id,
    string profile_id,string feature_id,string[] path_ids,string taper_variable_id,string part_operation,
    double source_radius,double profile_radius,bool driven_by_requested_source);
public sealed record DrawingRetainedSweepPart366(EntityRef body_ref,string body_id,string owner_document_id,string sketch_id,
    string constraint_id,string source_variable_id,string edited_source_id,double source_radius_before,double source_radius_after,
    DrawingRetainedSweepConsumer366[] consumers,string observation_scope);

// SDK-free comparison of the actual radius-only intent. Identifiers and owners
// are compared by the bridge's complete retained graph; each shape is keyed by
// its proved native source/element/profile identity before reaching this helper.
public sealed record DrawingRetainedShape366(string key,JsonElement geometry,bool driven);
public static class DrawingRetainedCircleEdit366
{
    public static bool Verify(DrawingRetainedShape366[] before,DrawingRetainedShape366[] after,double radius,
        Func<JsonElement,JsonElement,bool> same,Action cancel,out string reason)
    {
        reason="retained_shape_inventory_changed";
        if(!double.IsFinite(radius)||radius<=0)return false;
        if(before.Select(s=>s.key).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=before.Length||
            after.Select(s=>s.key).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=after.Length)return false;
        var current=after.ToDictionary(s=>s.key,StringComparer.OrdinalIgnoreCase);
        if(current.Count!=before.Length)return false;
        bool changed=false;
        foreach(var old in before)
        {
            cancel();if(!current.TryGetValue(old.key,out var actual)||old.driven!=actual.driven)return false;
            if(!old.driven)
            {if(!same(old.geometry,actual.geometry)){reason="unrequested_shape_changed: "+old.key;return false;}continue;}
            if(old.geometry.ValueKind!=JsonValueKind.Object||actual.geometry.ValueKind!=JsonValueKind.Object||
                !old.geometry.TryGetProperty("radius",out var oldRadius)||!actual.geometry.TryGetProperty("radius",out var actualRadius)||
                oldRadius.ValueKind!=JsonValueKind.Number||actualRadius.ValueKind!=JsonValueKind.Number||
                !double.IsFinite(oldRadius.GetDouble())||oldRadius.GetDouble()<=0||
                !double.IsFinite(actualRadius.GetDouble())||!DrawingProfileGeometry.Near(actualRadius.GetDouble(),radius))
            {reason="requested_source_or_contour_radius_not_observed: "+old.key;return false;}
            JsonElement WithoutRadius(JsonElement value)=>Wire.Element(value.EnumerateObject().Where(p=>p.Name!="radius")
                .ToDictionary(p=>p.Name,p=>p.Value.Clone(),StringComparer.Ordinal));
            if(!same(WithoutRadius(old.geometry),WithoutRadius(actual.geometry)))
            {reason="radius_edit_changed_center_plane_or_unrequested_geometry: "+old.key;return false;}
            changed=true;
        }
        reason=changed?"actual_source_projection_and_selected_contour_radii_observed; peers_retained":"no_proved_driven_shape";
        return changed;
    }
}
