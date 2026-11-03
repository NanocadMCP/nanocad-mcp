using System.Text.Json;
namespace NanoCAD.Contracts;

public sealed record ModelManufacturingEdit(string Kind,string[] Targets,string ExpectedLastItemId,string? FeatureId,JsonElement Definition):EditSpec(Kind,Targets);
public sealed record ModelManufacturingChange(string operation,EntityRef body_ref,string body_mc_id,string feature_id,JsonElement parameters,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelDatumPlaneRead353? datum_plane=null);
public static partial class ModifyValidation
{
    private static ModelManufacturingEdit ParseManufacturing(JsonElement item,string path,string[] handles,string kind)
    {
        Shape(item,path,"operation","handles","expected_last_item_id","feature_id","definition");
        if(handles.Length!=1)throw Invalid(path,"Select one native parametric body.");
        string Id(JsonElement o,string key){string s=Text(o,key,path,38);if(!Guid.TryParseExact(s,"B",out var g)||g==Guid.Empty)throw Invalid(path+"."+key,"Read a current native ID; do not invent it.");return g.ToString("B").ToUpperInvariant();}
        bool add=kind.StartsWith("add_",StringComparison.Ordinal);
        string last=Id(item,"expected_last_item_id");string? feature=add?null:Id(item,"feature_id");
        if(add&&item.TryGetProperty("feature_id",out _))throw Invalid(path,"feature_id is for updates only.");
        var d=Required(item,"definition",path);string at=path+".definition";
        double Num(string k,double min=0,double max=1e6)=>Number(Required(d,k,at),at+"."+k,min,max);
        bool Flag(string k){var v=Required(d,k,at);if(v.ValueKind is not JsonValueKind.True and not JsonValueKind.False)throw Invalid(at+"."+k,"Expected boolean.");return v.GetBoolean();}
        void Token(string k){var s=Text(d,k,at,64);if(s.Length!=64||s.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(at+"."+k,"Use the current model topology token.");}
        void Sources(){var a=Required(d,"source_feature_ids",at);if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()<1)throw Invalid(at,"Supply source feature IDs.");var seen=new HashSet<string>();foreach(var v in a.EnumerateArray()){if(v.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(v.GetString(),"B",out var g)||g==Guid.Empty||!seen.Add(g.ToString()))throw Invalid(at,"Use distinct native source IDs.");}}
        if(kind=="set_model_sketch_frame"){
            Shape(d,at,"edge_token","origin_edge_id","axis_edge_id","origin_point","axis_is_x","reverse_axis");
            Token("edge_token");Id(d,"origin_edge_id");Id(d,"axis_edge_id");
            if(Text(d,"origin_point",at,8) is not ("start" or "end" or "midpoint"))throw Invalid(at,"Use start, end or midpoint.");
            Flag("axis_is_x");Flag("reverse_axis");
        }
        else if(kind=="set_model_sketch_support"){
            if(d.TryGetProperty("kind",out _)){
                Shape(d,at,"kind","plane_id","expected_datum_token","recover_lost_support","expected_support_id","frame_reference_policy");
                if(Text(d,"kind",at,16)!="workplane")throw Invalid(at,"Use kind workplane to bind an existing native datum.");
                ModelSketchSupportValidation396.Parse(d,at,allowRecovery:true);
            }else{
                Shape(d,at,"reference_plane","offset","update_support_id");
                if(d.TryGetProperty("update_support_id",out _))Id(d,"update_support_id");
                if(Text(d,"reference_plane",at,2) is not ("XY" or "YZ" or "ZX"))throw Invalid(at,"Use XY, YZ or ZX.");
                Num("offset",-1e6);
            }
        }
        else if(kind=="set_model_extrusion_extent"){
            Shape(d,at,"extent","direction","distance","taper_angle","target_body_handle","face_token","face_id","replace_driver_formulas");
            if(d.TryGetProperty("replace_driver_formulas",out _))Flag("replace_driver_formulas");
            var extent=Text(d,"extent",at,16);if(extent is not "distance" and not "through_all" and not "to_face")throw Invalid(at,"Use distance, through_all or to_face.");
            if(Text(d,"direction",at,8) is not "positive" and not "negative")throw Invalid(at,"Use positive or negative relative to the sketch normal.");
            Num("distance",.001);Num("taper_angle",-Math.PI/2+1e-6,Math.PI/2-1e-6);
            if(extent=="to_face"){Validation.NormalizeHandle(Text(d,"target_body_handle",at,16));Token("face_token");Id(d,"face_id");}
            else if(d.TryGetProperty("target_body_handle",out _)||d.TryGetProperty("face_token",out _)||d.TryGetProperty("face_id",out _))throw Invalid(at,"Target face fields are only used by to_face.");
        }
        else if(kind.EndsWith("_loft",StringComparison.Ordinal)){
            Shape(d,at,"profile_ids","part_operation");var profiles=Required(d,"profile_ids",at);
            if(profiles.ValueKind!=JsonValueKind.Array||profiles.GetArrayLength()<2)throw Invalid(at,"Provide ordered native profile IDs.");
            var seen=new HashSet<Guid>();foreach(var v in profiles.EnumerateArray())if(v.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(v.GetString(),"B",out var g)||g==Guid.Empty||!seen.Add(g))throw Invalid(at,"Use distinct native profile IDs.");
            if(Text(d,"part_operation",at,8) is not "join" and not "cut")throw Invalid(at,"Use join or cut.");
        }
        else if(kind.EndsWith("_sweep",StringComparison.Ordinal))
        {
            Shape(d,at,"profile_id","path_handles","orientation","taper_angle","part_operation","replace_driver_formulas");Id(d,"profile_id");
            if(d.TryGetProperty("replace_driver_formulas",out _))Flag("replace_driver_formulas");
            var paths=Required(d,"path_handles",at);if(paths.ValueKind!=JsonValueKind.Array||paths.GetArrayLength()==0)throw Invalid(at,"Supply ordered path handles.");
            var seen=new HashSet<string>();foreach(var v in paths.EnumerateArray())if(v.ValueKind!=JsonValueKind.String||!seen.Add(Validation.NormalizeHandle(v.GetString()!)))throw Invalid(at,"Use distinct path handles.");
            string orientation=Text(d,"orientation",at,32);if(orientation is not "normal_to_path" and not "parallel_to_profile")throw Invalid(at,"Unknown orientation.");
            Num("taper_angle",-Math.PI/2+1e-6,Math.PI/2-1e-6);
            if(Text(d,"part_operation",at,8) is not "join" and not "cut")throw Invalid(at,"Use join or cut.");
        }
        else if(kind.EndsWith("_hole",StringComparison.Ordinal))
        {
            var fixedPosition=d.TryGetProperty("center",out _);
            if(add&&!fixedPosition)throw new DomainException("CAPABILITY_UNAVAILABLE","Native TwoEdges hole creation did not complete on this host. Use a fixed-center hole only when fixed placement expresses the task, or a supported retained sketch cut for positional dependencies. Existing hole editing has a separate scope.");
            var common=new[]{"diameter","through","depth","seat","seat_diameter","seat_depth","seat_angle_degrees","flat_bottom","bottom_angle_degrees"}.Concat(fixedPosition?new[]{"center"}:new[]{"offset1","offset2","sector"}).ToArray();
            Shape(d,at,(add?common.Concat(fixedPosition?new[]{"face_token","face_id"}:new[]{"face_token","face_id","edge_token","first_edge_id","second_edge_id"}):common).Append("replace_driver_formulas").ToArray());
            if(d.TryGetProperty("replace_driver_formulas",out _))Flag("replace_driver_formulas");
            Num("diameter",.001);bool through=Flag("through");Num("depth",through?0:.001);Flag("flat_bottom");Num("bottom_angle_degrees",.001,179.999);
            string seat=Text(d,"seat",at,16);if(seat is not "none" and not "counterbore" and not "countersink")throw Invalid(at,"Unknown hole seat.");
            double sd=Num("seat_diameter");Num("seat_depth");Num("seat_angle_degrees",.001,179.999);
            if(seat!="none"&&sd<=Num("diameter",.001))throw Invalid(at,"Seat diameter must exceed hole diameter.");
            if(seat=="counterbore"&&Num("seat_depth")<=0)throw Invalid(at,"Counterbore depth must be positive.");
            if(!through&&seat=="counterbore"&&Num("seat_depth")>=Num("depth"))throw Invalid(at,"Counterbore depth must be less than blind-hole depth.");
            if(fixedPosition){var c=Required(d,"center",at);if(c.ValueKind!=JsonValueKind.Array||c.GetArrayLength()!=3)throw Invalid(at,"center requires WCS XYZ.");foreach(var v in c.EnumerateArray())Number(v,at,-1e6,1e6);}
            else {Num("offset1",-1e6);Num("offset2",-1e6);var v=Required(d,"sector",at);if(v.ValueKind!=JsonValueKind.Number||!v.TryGetInt32(out int sector)||sector<0||sector>3)throw Invalid(at,"sector must be0..3 as defined by IppDefinition.");}
            if(add){Token("face_token");Id(d,"face_id");if(!fixedPosition){Token("edge_token");if(Id(d,"first_edge_id")==Id(d,"second_edge_id"))throw Invalid(at,"Use two distinct datum edges.");}}
        }
        else if(kind.EndsWith("_thread",StringComparison.Ordinal))
        {
            string[] fields=["pitch","right_handed","internal","full_length","length"];
            Shape(d,at,add?fields.Concat(new[]{"face_token","face_id","edge_token","front_edge_id"}).ToArray():fields);
            Num("pitch",.001);Flag("right_handed");Flag("internal");bool full=Flag("full_length");Num("length",full?0:.001);
            if(add){Token("face_token");Token("edge_token");Id(d,"face_id");Id(d,"front_edge_id");}
        }
        else if(kind.EndsWith("_circular_pattern",StringComparison.Ordinal))
        {
            Shape(d,at,add?["source_feature_ids","edge_token","axis_edge_id","axis","count","angle_degrees"]:
                ["count","angle_degrees","expected_count","expected_angle_degrees","axis","expected_axis_id","source_feature_ids","expected_source_feature_ids","replace_driver_formulas"]);
            var v=Required(d,"count",at);if(v.ValueKind!=JsonValueKind.Number||!v.TryGetInt32(out int n)||n<1)throw Invalid(at,"count must be a positive native Int32 integer.");
            Num("angle_degrees",double.MinValue,double.MaxValue);
            bool selector=d.TryGetProperty("axis",out var axis),edge=d.TryGetProperty("axis_edge_id",out _),edgeToken=d.TryGetProperty("edge_token",out _);
            if(selector)PatternDirection439(axis,at+".axis");
            if(add)
            {
                Sources();if(selector==(edge||edgeToken))throw Invalid(at,"Choose axis or the legacy axis_edge_id/edge_token pair.");
                if(!selector){Token("edge_token");Id(d,"axis_edge_id");}
            }
            else
            {
                if(selector!=d.TryGetProperty("expected_axis_id",out _))throw Invalid(at,"Axis replacement requires its observed expected_axis_id, and vice versa.");
                if(selector)Id(d,"expected_axis_id");
                if(d.TryGetProperty("expected_count",out var oldCount)&&(oldCount.ValueKind!=JsonValueKind.Number||!oldCount.TryGetInt32(out int old)||old<1))throw Invalid(at+".expected_count","Use the current positive native Int32 count.");
                if(d.TryGetProperty("expected_angle_degrees",out _))Num("expected_angle_degrees",double.MinValue,double.MaxValue);
                if(d.TryGetProperty("source_feature_ids",out _))
                {Sources();if(!d.TryGetProperty("expected_source_feature_ids",out _))throw Invalid(at,"Source replacement requires expected_source_feature_ids.");}
                if(d.TryGetProperty("expected_source_feature_ids",out var previous))
                {
                    if(previous.ValueKind!=JsonValueKind.Array||previous.GetArrayLength()<1)throw Invalid(at,"Use a nonempty original source feature group.");
                    var seen=new HashSet<Guid>();foreach(var value in previous.EnumerateArray())
                        if(value.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(value.GetString(),"B",out var g)||g==Guid.Empty||!seen.Add(g)||g.ToString("B").Equals(feature,StringComparison.OrdinalIgnoreCase))throw Invalid(at,"Use distinct original source feature IDs, excluding the pattern itself.");
                }
                if(d.TryGetProperty("source_feature_ids",out var desired)&&desired.EnumerateArray().Any(value=>value.GetString()!.Equals(feature,StringComparison.OrdinalIgnoreCase)))throw Invalid(at,"A pattern cannot be its own source.");
                if(d.TryGetProperty("replace_driver_formulas",out _))Flag("replace_driver_formulas");
            }
        }
        else
        {
            if(add&&kind=="add_model_mirror"&&d.TryGetProperty("datum_plane_id",out _))
            {
                Shape(d,at,"source_feature_ids","datum_plane_id","expected_datum_token");Sources();
                ModelDatumPlaneValidation353.Id(d,"datum_plane_id",at);ModelDatumPlaneValidation353.Token(d,"expected_datum_token",at);
                return new(kind,handles,last,feature,d.Clone());
            }
            Shape(d,at,add?["source_feature_ids","plane_id","reference_plane"]:["plane_id","reference_plane"]);
            bool named=d.TryGetProperty("reference_plane",out var reference),explicitId=d.TryGetProperty("plane_id",out _);
            if(named==explicitId)throw Invalid(at,"Choose reference_plane or an existing plane_id, not both.");
            if(named){if(reference.ValueKind!=JsonValueKind.String||reference.GetString() is not ("XY" or "YZ" or "ZX"))throw Invalid(at,"reference_plane must be XY, YZ or ZX.");}else Id(d,"plane_id");
            if(add)Sources();
        }
        return new(kind,handles,last,feature,d.Clone());
    }
}
