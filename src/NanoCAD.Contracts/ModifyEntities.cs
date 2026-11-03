using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace NanoCAD.Contracts;

public interface IWriteArguments : IOperationArguments
{
    string DocumentId { get; }
    string ExpectedRevision { get; }
    string Units { get; }
}
public static class WriteValidation
{
    public static IOperationArguments Parse(string operation, JsonElement input) => operation switch
    {
        "create_entities" => CreateValidation.Parse(input),
        "modify_entities" => ModifyValidation.Parse(input),
        "manage_document" => DocumentValidation.Parse(input),
        "modify_structure" => StructureWriteValidation.Parse(input),
        "set_model_parameter" => ModelParameterValidation.Parse(input),
        "set_view" => CameraChangeValidation.Parse(input),
        _ => throw new DomainException("INVALID_ARGUMENT", "Unknown write operation.")
    };
}

public abstract record EditSpec(string Operation, string[] Handles);
public sealed record MoveSpec(string[] Targets, Point2 Displacement,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane=null) : EditSpec("move", Targets);
public sealed record RotateSpec(string[] Targets, Point2 Center, double Angle,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane=null) : EditSpec("rotate", Targets);
public sealed record ScaleSpec(string[] Targets, Point2 Center, double Factor,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane=null) : EditSpec("scale", Targets);
public sealed record CopySpec(string[] Targets, Point2 Displacement,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane=null) : EditSpec("copy", Targets);
public sealed record EraseSpec(string[] Targets) : EditSpec("erase", Targets);
public sealed record PropertyChanges(string? Layer, int? ColorAci, string? Linetype, int? LineweightHundredthMm,
    double? LinetypeScale = null, bool? Visible = null, RgbColor? ColorRgb = null, TransparencyChange? Transparency = null);
public sealed record PropertiesSpec(string[] Targets, PropertyChanges Properties) : EditSpec("set_properties", Targets);
public sealed record ModifyArguments(string BridgeId, string DocumentId, string ExpectedRevision, string OperationId,
    string Units, EditSpec[] Operations) : IWriteArguments
{
    public string Operation => "modify_entities";
    public string TargetSpace { get; init; }="model";
    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    { Operation, BridgeId, DocumentId, ExpectedRevision, Units, TargetSpace, Operations = Operations.Cast<object>().ToArray() }, Wire.Json)));
}
public sealed record CopyMapping(int operation_index, EntityRef source, EntityRef created);
public sealed record ContourMapping(int operation_index,string operation,EntityRef source,EntityRef[] generated,bool source_erased);
public sealed record ModifiedEntities(string operation_id, string status, bool replayed, EntityRef[] modified,
    EntityRef[] erased, EntityRef[] created, CopyMapping[] copies, string undo_history,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ContourMapping[]? contour_results=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelFeatureMapping[]? model_history_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelSketchChange[]? model_sketch_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchConstraintChange[]? sketch_constraint_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelParameterNameChange[]? model_parameter_names=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelUserVariableChange[]? model_variables_created=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] DrawingCircleChange[]? drawing_circle_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] DrawingProfileChange[]? drawing_profile_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelPlacementChange[]? model_placement_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ContourAssemblyMapping[]? contour_assembly_results=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelRevolutionChange[]? model_revolution_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelEdgeFeatureChange[]? model_edge_features=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelPatternChange[]? model_pattern_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelPatternElementChange[]? model_pattern_element_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelShellChange[]? model_shell_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelFeatureSuppressionChange[]? model_feature_suppression_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SolidSliceMapping[]? solid_slice_results=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SolidSeparationMapping[]? solid_separation_results=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelManufacturingChange[]? manufacturing_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SolidBooleanChange[]? solid_boolean_results=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] RegionBooleanChange[]? region_boolean_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelFeatureRevolutionMapping[]? model_revolution_features=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] MTextFormatChange[]? mtext_format_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ContourCornerResult343[]? contour_corner_results=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SolidStockChange353[]? solid_stock_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] TableTopologyChange354[]? table_topology_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ToleranceFrameChange356[]? tolerance_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SolidImprintChange393[]? solid_imprint_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] RepetitionResult407[]? repetition_results=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelSketchVisibilityChange436[]? model_sketch_visibility_changes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] TextFormatChange442[]? text_format_changes=null);

public static partial class ModifyValidation
{
    public static readonly int[] Lineweights = { -3, -2, -1, 0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211 };
    private static DomainException Invalid(string path, string message, string code = "INVALID_ARGUMENT") => new(code, path + ": " + message, false, new { path });
    private static void Shape(JsonElement value, string path, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(path, "Expected object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal)) throw Invalid(path + "." + property.Name, "Unknown or duplicate field.");
    }
    private static JsonElement Required(JsonElement value, string name, string path) => value.TryGetProperty(name, out var item) ? item : throw Invalid(path + "." + name, "Required field is missing.");
    private static string Text(JsonElement value, string name, string path, int max = int.MaxValue)
    {
        var item = Required(value, name, path);
        if (item.ValueKind != JsonValueKind.String) throw Invalid(path + "." + name, "Expected string.");
        string text;
        try { text = item.GetString()!; }
        catch (InvalidOperationException) { throw Invalid(path + "." + name, "Invalid Unicode string."); }
        if (text.Length == 0 || text.Length > max || text.Any(char.IsControl))
            throw Invalid(path + "." + name, "Invalid string.");
        return text;
    }
    private static double Number(JsonElement value, string path, double min = -1e9, double max = 1e9)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || number < min || number > max)
            throw Invalid(path, "Number is not finite or outside the supported range.", "INVALID_GEOMETRY");
        return number == 0 ? 0 : number;
    }
    private static Point2 Point(JsonElement value, string name, string path)
    {
        var item = Required(value, name, path);
        if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2) throw Invalid(path + "." + name, "Expected WCS XY pair.", "INVALID_GEOMETRY");
        return new(Number(item[0], path + "." + name + "[0]"), Number(item[1], path + "." + name + "[1]"));
    }
    public static ModifyArguments Parse(JsonElement value)
    {
        Shape(value, "$", "bridge_instance_id", "document_id", "operation_id", "expected_revision", "units", "operations", "target_space");
        if (System.Text.Encoding.UTF8.GetByteCount(value.GetRawText()) > Wire.MaxRequestBytes - 1024) throw Invalid("$", "Request exceeds IPC budget.");
        string Id(string name)
        {
            if (!Guid.TryParseExact(Text(value, name, "$", 36), "D", out var id) || id == Guid.Empty) throw Invalid("$." + name, "Expected nonempty UUID.");
            return id.ToString("D");
        }
        string bridge = Id("bridge_instance_id"), document = Id("document_id"), operation = Id("operation_id");
        string revision = Text(value, "expected_revision", "$", 20), units = Text(value, "units", "$", 32);
        if (revision.Any(c => c is < '0' or > '9') || !ulong.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revisionNumber)) throw Invalid("$.expected_revision", "Expected decimal uint64 string.");
        if (units != "Millimeters") throw Invalid("$.units", "Explicit Millimeters required.", "UNSUPPORTED_UNITS");
        var operations = Required(value, "operations", "$");
        if (operations.ValueKind != JsonValueKind.Array || operations.GetArrayLength()<1) throw Invalid("$.operations", "Expected a nonempty operation list.");
        var edits = new List<EditSpec>(); var all = new HashSet<string>(StringComparer.Ordinal); var erased = new HashSet<string>(StringComparer.Ordinal);
        int handleUses = 0, copies = 0;
        foreach (var item in operations.EnumerateArray())
        {
            string path = "$.operations[" + edits.Count + "]";
            if (item.ValueKind != JsonValueKind.Object) throw Invalid(path, "Expected object.");
            // The closed dispatcher defines valid names; the request byte
            // budget already bounds their storage. Published names can grow.
            string kind = Text(item, "operation", path);
            var targets = Required(item, "handles", path);
            if (targets.ValueKind != JsonValueKind.Array || targets.GetArrayLength()<1) throw Invalid(path + ".handles", "Expected a nonempty handle list.");
            var handles = new List<string>(); var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in targets.EnumerateArray())
            {
                if (target.ValueKind != JsonValueKind.String) throw Invalid(path + ".handles", "Expected hex strings.");
                string handle = Validation.NormalizeHandle(target.GetString()!);
                if (!unique.Add(handle)) throw Invalid(path + ".handles", "Duplicate normalized handle in an operation.");
                if (erased.Contains(handle)) throw Invalid(path + ".handles", "An earlier operation erases this handle.", "INVALID_OPERATION_ORDER");
                handles.Add(handle); all.Add(handle);
            }
            handleUses += handles.Count;
            
            var ids = handles.ToArray();
            if(kind is "trim_curve" or "split_curve" or "extend_curve" && NativeContourFinishingValidation342.NativeMode(item,path))
            {
                var native=NativeContourFinishingValidation342.Parse(item,path,ids,kind);
                foreach(string boundary in native.BoundaryHandles)
                {
                    if(erased.Contains(boundary))throw Invalid(path+".boundary_handles","An earlier operation erases this boundary.","INVALID_OPERATION_ORDER");
                    all.Add(boundary);handleUses++;
                }
                edits.Add(native);
                if(native.EraseSource)erased.Add(ids[0]);
                continue;
            }
            switch (kind)
            {
                case "set_point_position":edits.Add(ParsePointPosition408(item,path,ids));break;
                case "set_legacy_polyline":edits.Add(ParseLegacyPolyline408(item,path,ids));break;
                case "copy_transform_2d":edits.Add(ParseCopyTransform407(item,path,ids));break;
                case "polar_repeat":edits.Add(ParsePolarRepeat407(item,path,ids));break;
                case "set_draw_order":edits.Add(DrawOrderValidation407.Parse(item,path,ids));break;
                case "transform_point_cloud":edits.Add(ParsePointCloudPlacement(item,path,ids));break;
                case "set_mleader_vertices":edits.Add(MLeaderGeometryValidation388.Parse(item,path,ids));break;
                case "explode_native":edits.Add(NativeExplodeValidation386.Parse(item,path,ids));break;
                case "stretch_curve":edits.Add(item.TryGetProperty("selection_window",out _) ? NativeWindowStretchValidation387.Parse(item,path,ids) : NativeStretchValidation345.Parse(item,path,ids));break;
                case "reorder_model_feature":edits.Add(ModelFeatureReorderValidation345.Parse(item,path,ids));break;
                case "delete_model_feature":edits.Add(ModelFeatureDeletionValidation378.Parse(item,path,ids));break;
                case "set_sketch_constraint_parameter":edits.Add(ModelSketchConstraintParameterValidation379.Parse(item,path,ids));break;
                case "create_associative_rectangular_array": case "set_associative_rectangular_array":edits.Add(AssociativeArrayValidation431.Parse(item,path,ids,kind));break;
                case "create_associative_polar_array": case "set_associative_polar_array": case "create_associative_path_array": case "set_associative_path_array":edits.Add(AssociativeArrayValidation441.Parse(item,path,ids,kind));break;
                case "set_wipeout_boundary":edits.Add(WipeoutValidation431.ParseEdit(item,path,ids));break;
                case "edit_mleader_topology":edits.Add(MLeaderTopologyValidation431.Parse(item,path,ids));break;
                case "delete_sketch_constraints":edits.Add(ModelSketchConstraintRemovalValidation430.Parse(item,path,ids));break;
                case "set_model_sketch_visibility":edits.Add(ModelSketchVisibilityValidation436.Parse(item,path,ids));break;
                case "create_model_datum_plane":edits.Add(ModelDatumPlaneValidation353.Parse(item,path,ids));break;
                case "set_model_datum_plane":edits.Add(ModelDatumPlaneRevisionValidation354.Parse(item,path,ids));break;
                case "edit_model_profile_elements":edits.Add(ModelProfileElementsValidation358.Parse(item,path,ids));break;
                case "set_model_profile_regions":edits.Add(ModelProfileRegionsValidation350.Parse(item,path,ids));break;
                case "set_model_extrusion_profile":edits.Add(ModelProfileRebindValidation349.Parse(item,path,ids));break;
                case "fillet_curves":case "chamfer_curves":
                    edits.Add(IndependentContourCornersValidation343.Parse(item,path,ids,kind));break;
                case "fill_table_formula_range":edits.Add(TableFormulaRangeValidation350.Parse(item,path,ids));break;
                case "set_table_cell_formula":
                    edits.Add(TableFormulaValidation343.Parse(item,path,ids));break;
                case "set_mtext_format":edits.Add(ParseMTextFormat(item,path,ids));break;
                case "set_text_format":edits.Add(TextFormatValidation442.Parse(item,path,ids));break;
                case "set_mleader_instance":edits.Add(MLeaderInstanceValidation442.Parse(item,path,ids));break;
                case "edit_table_contents":edits.Add(TableContentsValidation364.Parse(item,path,ids));break;
                case "set_table_cell_value":case "set_table_style":
                    edits.Add(TableValueEditingValidation336.Parse(item,path,ids));break;
                case "set_table_cell_layout":
                    edits.Add(TableCellLayoutValidation340.Parse(item,path,ids));break;
                case "set_table_grid_layout":
                    edits.Add(TableGridLayoutValidation341.Parse(item,path,ids));break;
                case "set_tolerance":edits.Add(ExtendedAnnotationValidation356.ParseTolerance(item,path,ids));break;
                case "associate_dimension":case "update_dimension_association":case "detach_dimension_association":edits.Add(DimensionAssociationValidation385.Parse(item,path,ids,kind));break;
                case "set_dimension_display":case "set_dimension_definition":
                    edits.Add(DimensionEditingValidation341.Parse(item,path,ids));break;
                case "create_model_sketch":case "create_model_profile_on_sketch":case "add_model_sweep_from_profile":case "add_model_revolution_from_profile":
                    edits.Add(ModelSketchLifecycleValidation362.Parse(item,path,ids));break;
                case "add_model_loft_from_profiles":
                    edits.Add(ModelProfileLoftValidation446.Parse(item,path,ids));break;
                case "create_model_profile":case "add_model_extrusion_from_profile":
                    edits.Add(ModelProfileLifecycleValidation340.Parse(item,path,ids));break;
                case "insert_table_rows":case "delete_table_rows":case "insert_table_columns":case "delete_table_columns":
                case "merge_table_cells":case "unmerge_table_cells":case "set_table_cell_format":case "set_table_cell_text":
                    edits.Add(TableEditingValidation.Parse(item,path,ids));break;
                case "set_dimension_format":edits.Add(ParseDimensionFormat(item,path,ids));break;
                case "set_hatch_properties":edits.Add(ParseHatchProperties(item,path,ids));break;
                case "set_hatch_fill":edits.Add(HatchFillValidation377.Parse(item,path,ids));break;
                case "set_hatch_boundary":edits.Add(HatchBoundaryValidation349.Parse(item,path,ids));break;
                case "set_hatch_association":edits.Add(HatchAssociationValidation358.Parse(item,path,ids));break;
                case "imprint_solid":edits.Add(ParseSolidImprint393(item,path,ids));break;
                case "boolean_solid":edits.Add(ParseSolidBooleanEdit(item,path,ids));break;
                case "replace_solid":edits.Add(ParseSolidReplacement409(item,path,ids));break;
                case "boolean_region":edits.Add(ParseRegionBooleanEdit(item,path,ids));break;
                case "mirror":var mirror=ParseMirror2d(item,path,ids);edits.Add(mirror);if(mirror.Copy)copies+=ids.Length;break;
                case "set_model_extrusion_extent":edits.Add(ModelExtrusionExtentsValidation351.Selects(item)?ModelExtrusionExtentsValidation351.Parse(item,path,ids):ParseManufacturing(item,path,ids,kind));break;
                case "set_model_sketch_frame":case "set_model_sketch_support":case "add_model_loft":case "set_model_loft":case "add_model_sweep":case "set_model_sweep":case "add_model_hole":case "set_model_hole":case "add_model_thread":case "set_model_thread":case "add_model_circular_pattern":case "set_model_circular_pattern":case "add_model_mirror":case "set_model_mirror":edits.Add(ParseManufacturing(item,path,ids,kind));break;
                case "set_model_revolution":edits.Add(ParseModelRevolution(item,path,ids));break;
                case "join_curves":case "split_curve":case "explode_polyline":edits.Add(ParseContourAssembly(item,path,ids,kind));break;
                case "add_model_placement":case "set_model_placement":edits.Add(ParseModelPlacement(item,path,ids,kind));break;
                case "add_model_extrusion":edits.Add(ParseModelFeature(item,path,ids));break;
                case "add_model_revolution":edits.Add(ParseModelFeatureRevolution(item,path,ids));break;
                case "add_model_rectangular_pattern":case "set_model_rectangular_pattern":edits.Add(ParseModelPattern(item,path,ids,kind));break;
                case "set_model_pattern_element":edits.Add(ParseModelPatternElement(item,path,ids));break;
                case "add_model_shell":edits.Add(ParseModelShell(item,path,ids));break;
                case "set_model_shell_thickness":edits.Add(ParseModelShellThickness(item,path,ids));break;
                case "set_model_feature_suppressed":edits.Add(ParseModelFeatureSuppression(item,path,ids));break;
                case "rebuild_model":edits.Add(ParseModelRebuild419(item,path,ids));break;
                case "add_model_fillet":case "add_model_chamfer":edits.Add(ParseModelEdgeFeature(item,path,ids,kind));break;
                case "set_model_sketch_geometry":edits.Add(ParseModelSketch(item,path,ids));break;
                case "add_sketch_constraints":edits.Add(ParseSketchConstraints(item,path,ids));break;
                case "add_drawing_radius":case "set_drawing_radius":case "extrude_drawing_circle":edits.Add(ParseDrawingCircle(item,path,ids,kind));break;
                case "create_drawing_variable":case "name_drawing_parameter":case "set_drawing_expression":case "remove_drawing_constraints":case "remove_drawing_variable":
                case "add_drawing_constraints":case "set_drawing_dimension":case "extrude_drawing_profile":case "revolve_drawing_profile":edits.Add(ParseDrawingProfile(item,path,ids,kind));break;
                case "create_model_variable":edits.Add(ParseUserVariable(item,path,ids));break;
                case "name_model_parameter":edits.Add(ParseParameterName(item,path,ids));break;
                case "trim_curve":
                case "extend_curve":
                case "offset_curve":
                    if(kind=="offset_curve"&&item.TryGetProperty("side",out var nativeSide)&&nativeSide.ValueKind==JsonValueKind.String&&NativeContourOffsetValidation341.HandlesSide(Text(item,"side",path,16)))
                    {
                        edits.Add(NativeContourOffsetValidation341.Parse(item,path,ids));
                        break;
                    }
                    var contour=ParseContourEdit(item,path,ids);edits.Add(contour);
                    foreach(var boundary in EditReferences.Boundaries(contour))
                    {
                        if(erased.Contains(boundary))throw Invalid(path+".boundary_handles","An earlier operation erases this boundary.","INVALID_OPERATION_ORDER");
                        all.Add(boundary);handleUses++;
                    }
                    
                    if(kind=="trim_curve")erased.Add(ids[0]);
                    copies+=kind=="trim_curve"?2:kind=="offset_curve"?1:0;
                    
                    break;
                case "set_polyline_geometry":
                case "set_polyline_corners":
                case "reverse_polyline":
                    var topology = ParsePolylineTopology(item, path, ids, kind);
                    edits.Add(topology); break;
                case "set_curve_geometry":
                    var curveEdit = ParseCurveGeometry(item,path,ids);
                    edits.Add(curveEdit); break;
                case "transform_solid":
                    
                    
                    edits.Add(ParseSolidEdit(item, path, ids)); break;
                case "separate_solid":
                    Shape(item,path,"operation","handles");edits.Add(new SeparateSolidSpec(ids));break;
                case "slice_solid":
                    edits.Add(ParseSolidSlice(item,path,ids));break;
                case "offset_solid":
                    edits.Add(ParseSolidOffset353(item,path,ids));break;
                case "fillet_solid":case "chamfer_solid":case "shell_solid":
                    
                    edits.Add(ParseSolidFinishingEdit(item,path,ids,kind));break;
                case "offset_faces": case "presspull_faces": case "extrude_faces": case "extrude_faces_along_path": case "remove_faces": case "transform_faces": case "taper_faces":
                    edits.Add(ParseFaceEdit(item,path,ids,kind));break;
                case "drill_solid":
                    
                    edits.Add(ParseSolidDrill(item,path,ids));break;
                case "set_polyline_vertices":
                    var verticesEdit = ParsePolylineVertices(item, path, ids);
                    edits.Add(verticesEdit); break;
                case "set_hatch_pattern":
                    var hatch=ParseHatchPattern(item,path,ids);
                    edits.Add(hatch);break;
                case "set_dimension_line":
                    var dimensionLine=ParseDimensionLine(item,path,ids);
                    edits.Add(dimensionLine);break;
                case "set_dimension_text_position":
                    edits.Add(ParseDimensionTextPosition(item,path,ids));break;
                case "replace_dimension_text":
                    var dimensionText=ParseDimensionText(item,path,ids);
                    edits.Add(dimensionText);break;
                case "replace_mtext":
                    var mtextEdit=ParseReplaceMText(item,path,ids);
                    edits.Add(mtextEdit);break;
                case "resize_table":
                    var tableSize=ParseTableSize(item,path,ids);
                    edits.Add(tableSize);break;
                case "replace_mleader_text":
                    var mleaderEdit=ParseMLeaderText(item,path,ids);
                    edits.Add(mleaderEdit);break;
                case "replace_table_text":
                    var tableEdit=ParseTableText(item,path,ids);
                    edits.Add(tableEdit);break;
                case "replace_text":
                    var textEdit=ParseReplaceText(item,path,ids);
                    edits.Add(textEdit);break;
                case "move":
                case "copy":
                    Shape(item, path, "operation", "handles", "displacement", "workplane");
                    var delta = Point(item, "displacement", path);
                    if (kind == "copy")
                    {
                        copies += ids.Length;
                        
                        edits.Add(new CopySpec(ids, delta, PlaneCurveTransforms394.Parse(item)));
                    }
                    else edits.Add(new MoveSpec(ids, delta, PlaneCurveTransforms394.Parse(item)));
                    break;
                case "rotate":
                    Shape(item, path, "operation", "handles", "center", "angle", "workplane");
                    edits.Add(new RotateSpec(ids, Point(item, "center", path), Number(Required(item, "angle", path), path + ".angle", -2 * Math.PI, 2 * Math.PI), PlaneCurveTransforms394.Parse(item))); break;
                case "scale":
                    Shape(item, path, "operation", "handles", "center", "factor", "workplane");
                    edits.Add(new ScaleSpec(ids, Point(item, "center", path), Number(Required(item, "factor", path), path + ".factor", 1e-6, 1e6), PlaneCurveTransforms394.Parse(item))); break;
                case "erase":
                    Shape(item, path, "operation", "handles");
                    erased.UnionWith(ids); edits.Add(new EraseSpec(ids)); break;
                case "set_properties":
                    Shape(item, path, "operation", "handles", "properties");
                    var properties = Required(item, "properties", path); string pp = path + ".properties";
                    Shape(properties, pp, "layer", "color_aci", "linetype", "lineweight_hundredth_mm", "linetype_scale", "visible", "color_rgb", "transparency");
                    if (!properties.EnumerateObject().Any()) throw Invalid(pp, "At least one property is required.");
                    int? Integer(string name)
                    {
                        if (!properties.TryGetProperty(name, out var property)) return null;
                        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var number)) throw Invalid(pp + "." + name, "Expected integer.");
                        return number;
                    }
                    int? aci = Integer("color_aci"), weight = Integer("lineweight_hundredth_mm");
                    if (aci is < 0 or > 256) throw Invalid(pp + ".color_aci", "Use 0 (ByBlock), 1–255 (ACI), 256 (ByLayer).");
                    if (weight.HasValue && !Lineweights.Contains(weight.Value)) throw Invalid(pp + ".lineweight_hundredth_mm", "Unsupported native lineweight value.");
                    double? linetypeScale = null;
                    if (properties.TryGetProperty("linetype_scale", out var scaleValue))
                    {
                        if (scaleValue.ValueKind != JsonValueKind.Number || !scaleValue.TryGetDouble(out var scale) || !double.IsFinite(scale) || scale <= 0)
                            throw Invalid(pp + ".linetype_scale", "Expected a finite positive linetype scale.");
                        linetypeScale = scale;
                    }
                    bool? visible = null;
                    if (properties.TryGetProperty("visible", out var visibleValue))
                    {
                        if (visibleValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(pp + ".visible", "Expected boolean.");
                        visible = visibleValue.GetBoolean();
                    }
                    var appearance = AppearanceValidation.Parse(properties, pp);
                    edits.Add(new PropertiesSpec(ids, new(properties.TryGetProperty("layer", out _) ? Text(properties, "layer", pp, 255) : null,
                        aci, properties.TryGetProperty("linetype", out _) ? Text(properties, "linetype", pp, 255) : null, weight, linetypeScale, visible, appearance.ColorRgb, appearance.Transparency))); break;
                default: throw Invalid(path + ".operation", "Unknown edit operation. Read the published modify_entities schema or its exact operation guidance before writing.");
            }
        }
        if(edits.Any(edit=>edit is ContourAssemblySpec)&&edits.Count!=1)throw Invalid("$.operations","Contour assembly/split/explode must be the only operation in its request; use returned handles in the next request.");
        if(edits.Any(edit=>edit is SliceSolidSpec or SeparateSolidSpec)&&edits.Count!=1)throw Invalid("$.operations","Solid slicing must be the sole operation; use returned pieces in subsequent edits.");
        if(edits.Any(edit=>edit is SolidImprintEditSpec393)&&edits.Count!=1)throw Invalid("$.operations","Native solid imprint must be the sole operation; read fresh topology before a later face edit.");
        if(edits.Any(edit=>edit is SolidReplacement409)&&edits.Count!=1)throw Invalid("$.operations","Native solid replacement must be the sole operation; the source is a read-only input.");
        if(edits.Any(edit=>edit is SolidBooleanEditSpec)&&edits.Count!=1)throw Invalid("$.operations","In-place solid boolean must be the sole operation; tool shapes are read-only inputs.");
        if(edits.Any(edit=>edit is TableEditingSpec)&&edits.Count!=1)throw Invalid("$.operations","Native table lifecycle/format edit requires one operation and its current table_token; read the next token after commit.");
        if(edits.Any(edit=>edit is ModelManufacturingEdit or ModelFeatureExtrusionSpec or ModelPatternSpec or ModelPatternElementSpec or ModelShellSpec or ModelShellThicknessSpec or ModelFeatureSuppressionSpec or ModelRebuildSpec419 or ModelEdgeFeatureSpec or ModelPlacementEdit or ModelRevolutionEdit)&&edits.Count!=1)throw Invalid("$.operations","Native history or placement operation must be the only operation in its request.");
        if(edits.Any(edit=>edit is ModelSketchEdit)&&edits.Any(edit=>edit is not ModelSketchEdit))
            throw Invalid("$.operations","A model sketch batch requires only sketch edits and no other operations.");
        if(edits.Any(edit=>edit is SketchConstraintsEdit)&&edits.Count!=1)throw Invalid("$.operations","Sketch constraints must be the only operation in the request.");
        if(edits.Any(edit=>edit is DrawingCircleEdit)&&edits.Count!=1)throw Invalid("$.operations","A drawing driver or linked extrusion must be the only operation in the request.");
        if(edits.Any(edit=>edit is DrawingProfileEdit)&&edits.Count!=1)throw Invalid("$.operations","A drawing profile/constraint operation must be the only operation in the request.");
        if(edits.Any(edit=>edit is ModelUserVariableEdit)&&edits.Count!=1)throw Invalid("$.operations","Creating a document variable must be the only operation.");
        if(edits.Any(edit=>edit is ModelParameterNameEdit)&&edits.Count!=1)throw Invalid("$.operations","Parameter naming must be the only operation in the request.");
        if(edits.Any(e=>e is SolidFaceEditSpec)&&edits.Count!=1)throw Invalid("$.operations","Face edits require a fresh topology snapshot between operations.");
        foreach(var machining in edits.Where(edit=>edit is SolidFinishingEditSpec or SolidOffsetEdit353 or SolidDrillEditSpec))
            if(edits.Any(other=>!ReferenceEquals(other,machining)&&other.Handles.Contains(machining.Handles[0])))
                throw Invalid("$.operations","A machined solid cannot have another edit in the same batch; inspect its geometry after commit.","INVALID_OPERATION_ORDER");
        DrawOrderValidation407.Batch(edits);
        HatchPropertiesValidation.Batch(edits);
        HatchFillValidation377.Batch(edits);
        NativeExplodeValidation386.Batch(edits);
        DimensionAssociationValidation385.Batch(edits);
        HatchBoundaryValidation349.Batch(edits);
        HatchAssociationValidation358.Batch(edits);
        MTextFormatValidation.Batch(edits);
        TextFormatValidation442.Batch(edits);
        MLeaderInstanceValidation442.Batch(edits);
        TableContentsValidation364.Batch(edits);
        TableValueEditingValidation336.Batch(edits);
        TableCellLayoutValidation340.Batch(edits);
        TableGridLayoutValidation341.Batch(edits);
        DimensionEditingValidation341.Batch(edits);
        foreach(var frame in edits.OfType<ToleranceEdit356>())if(edits.Count(e=>e.Handles.Intersect(frame.Handles,StringComparer.Ordinal).Any())!=1)throw new DomainException("INVALID_OPERATION_ORDER","Each tolerance frame requires its own operation only in this request.");
        NativeContourOffsetValidation341.Batch(edits);
        NativeContourFinishingValidation342.Batch(edits);
        IndependentContourCornersValidation343.Batch(edits);
        LegacyPolylineValidation408.Batch(edits);
        TableFormulaValidation343.Batch(edits);
        TableFormulaRangeValidation350.Batch(edits);
        RegionBooleanValidation.Batch(edits);
        ModelFeatureRevolutionValidation.Batch(edits);
        ModelProfileLifecycleValidation340.Batch(edits);
        ModelSketchLifecycleValidation362.Batch(edits);
        NativeStretchValidation345.Batch(edits);
        MLeaderGeometryValidation388.Batch(edits);
        ModelFeatureReorderValidation345.Batch(edits);
        ModelFeatureDeletionValidation378.Batch(edits);
        ModelSketchConstraintParameterValidation379.Batch(edits);
        ModelSketchConstraintRemovalValidation430.Batch(edits);
        ModelSketchVisibilityValidation436.Batch(edits);
        MLeaderTopologyValidation431.Batch(edits);
        WipeoutValidation431.Batch(edits);
        AssociativeArrayValidation431.Batch(edits);
        ModelProfileRebindValidation349.Batch(edits);
        ModelDatumPlaneValidation353.Batch(edits);
        ModelDatumPlaneRevisionValidation354.Batch(edits);
        ModelProfileRegionsValidation350.Batch(edits);
        ModelProfileElementsValidation358.Batch(edits);
        ModelExtrusionExtentsValidation351.Batch(edits);
        var result=new ModifyArguments(bridge, document, revisionNumber.ToString(CultureInfo.InvariantCulture), operation, units, edits.ToArray()){TargetSpace=WriteSpace.Parse(value)};
        WriteSpace.Check(result);return result;
    }
}
