using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Guidance from already-read history, not an SDK probe or a claim that
// preflight will accept every consumer/topology in this body.
internal static class ModelEditRoutes
{
    internal static object Describe(IEnumerable<object> nodes)
    {
        var routes=new List<object>();bool views=false,retained=false;
        foreach(var value in nodes)
        {
            var node=Wire.Element(value);string type=node.GetProperty("runtime_type").GetString()!;
            views|=type.EndsWith(".DrawingView",StringComparison.Ordinal);
            if(!node.TryGetProperty("parameters",out var p)||p.ValueKind!=JsonValueKind.Object||!p.TryGetProperty("kind",out var k))continue;
            string? kind=k.GetString();retained|=kind=="projection";
            string[] operations=kind switch
            {
                "sketch"=>new[]{"set_model_sketch_support","set_model_sketch_frame","set_model_sketch_geometry","create_model_profile_on_sketch","add_sketch_constraints","delete_sketch_constraints","set_sketch_constraint_parameter","set_model_parameter"},
                "profile"=>p.TryGetProperty("profile_editing",out var profileEditing)&&
                    profileEditing.TryGetProperty("reusable",out var reusable)&&reusable.ValueKind==JsonValueKind.True
                    ?new[]{"add_model_extrusion_from_profile","add_model_sweep_from_profile","add_model_revolution_from_profile","add_model_loft_from_profiles"}:Array.Empty<string>(),
                "extrusion"=>new[]{"set_model_parameter","set_model_extrusion_extent","set_model_extrusion_profile","set_model_profile_regions","edit_model_profile_elements","delete_model_feature"},
                "revolution" or "fillet" or "chamfer"=>new[]{"set_model_parameter","delete_model_feature"},
                "sweep"=>new[]{"set_model_sweep"},
                "loft"=>p.TryGetProperty("loft_type",out var loftType)&&loftType.GetString()=="Regular"?new[]{"set_model_loft"}:Array.Empty<string>(),
                "hole"=>new[]{"set_model_hole"},
                "thread"=>new[]{"set_model_thread"},
                "circular_pattern"=>new[]{"set_model_circular_pattern"},
                "mirror"=>new[]{"set_model_mirror"},
                "shell"=>new[]{"set_model_shell_thickness","delete_model_feature"},
                "rectangular_pattern"=>new[]{"set_model_rectangular_pattern","set_model_pattern_element"},
                _=>Array.Empty<string>()
            };
            if(operations.Length>0)routes.Add(new{feature_id=node.GetProperty("mc_id").GetString(),kind,candidate_operations=operations});
        }
        return new{scope="route_guidance_not_preflight_or_full_capability_inventory",features=routes,
            retained_sources_present=retained,
            source_driver_route=retained?"Inspect source handles and drawing constraints; use set_drawing_dimension/set_drawing_expression for persistent source drivers. Coordinate editing is not a replacement for a relation.":null,
            documentation_present=views,
            documentation_follow_up=views?"Review regenerated projections/sections and update independent dimensions after a significant shape change.":null,
            internal_sketch_constraints="native_scoped_path: inspect current native inputs/relations/control tokens; initial annotation value alone does not prove driving behavior",
            preflight="Fresh history/topology, parameter identity, owner and consumer checks still apply. Readable variables are not all writable. Profile reuse checks actual consumers; feature deletion needs a fresh deletion token and eligible owning lifecycle."};
    }
}
