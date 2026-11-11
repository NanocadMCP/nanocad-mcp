using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record LayerChanges(int? ColorAci, string? Linetype, int? LineweightHundredthMm,
    bool? IsOff, bool? IsFrozen, bool? IsLocked, bool? IsPlottable, RgbColor? ColorRgb = null, TransparencyChange? Transparency = null, string? Description = null);
public abstract record StructureEdit(string Operation)
{
    public virtual object CanonicalValue() => this;
}
public sealed record CreateLayerEdit(string Name, LayerChanges Properties) : StructureEdit("create_layer");
public sealed record SetLayerEdit(string Handle, LayerChanges Properties) : StructureEdit("set_layer_properties");
public sealed record SetCurrentLayerEdit(string Handle, string ExpectedCurrentHandle) : StructureEdit("set_current_layer");
public sealed record SetAttributeEdit(string BlockHandle, string AttributeHandle, string ExpectedText, string Text, string ContentType = "single_line",
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? OwnerDefinitionHandle = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ExpectedDefinitionToken = null) : StructureEdit("set_attribute_text");
public sealed record StructureWriteArguments(string BridgeId, string DocumentId, string ExpectedRevision,
    string OperationId, string Units, StructureEdit[] Operations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetSpace = null) : IWriteArguments
{
    [JsonIgnore] public string EffectiveTargetSpace => TargetSpace ?? "model";
    public string Operation => "modify_structure";
    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    { Operation, BridgeId, DocumentId, ExpectedRevision, Units, target_space = EffectiveTargetSpace, Operations = Operations.Select(edit => edit.CanonicalValue()).ToArray() }, Wire.Json)));
}
public sealed record StructureChild(EntityRef @ref, string kind, int source_index, string? tag);
public sealed record StructureChange(int operation_index, string operation, EntityRef @ref, string kind, string name, bool created,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StructureChild[]? children = null);
public sealed record ModifiedStructure(string operation_id, string status, bool replayed, StructureChange[] changes, string undo_history,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ViewportLifecycleChange[]? viewport_lifecycle_results = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TableStyleChange337[]? table_style_changes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DrawingViewLifecycleChange345[]? drawing_view_lifecycle_changes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PaperDrawingViewCreation356[]? drawing_view_creation_results = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ViewportClippingChange358[]? viewport_clipping_changes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BlockLibraryDwgResult[]? block_library_results = null);

public static class StructureWriteValidation
{
    public static readonly int[] LayerLineweights = ModifyValidation.Lineweights.Where(w => w == -3 || w >= 0).ToArray();
    // These operations change document settings, without coordinates in drawing units.
    public static bool IsSettingsOnly(IEnumerable<StructureEdit> operations) => operations.All(op => op is
        DrawingGroupEdit431 or ReferenceLifecycle385 or SetRasterImageDisplay390 or SetRasterImageClip391 { Mode: "clear" } or SetViewportClipping358 or CreateLayerEdit or SetLayerEdit or RenameLayerEdit or RenameBlockDefinition349 or SetBlockDefinitionText391 or SetBlockDefinitionMembers392 { AddEntities: [] } or NativeStyleLifecycle352 or SetCurrentLayerEdit or SetTextStyleFontEdit or RenameLayoutEdit or StructureLifecycleEdit or DeleteDrawingViewsEdit or DeleteViewportEdit or SetAttributeEdit or SetDrawingViewPropertiesEdit { Origin: null } ||
        op is SetLayoutPropertiesEdit or SetDrawingViewSources345 or RenameDrawingView345 ||
        op is TableStyleEdit337 tableStyle && TableStyleValidation337.IsSettingsOnly(tableStyle) ||
        op is MLeaderStyleEdit337 leaderStyle && MLeaderStyleValidation337.IsSettingsOnly(leaderStyle) ||
        op is SetViewportViewEdit { Properties: { ViewCenter: null, ViewHeight: null, TwistAngle: null, Center: null, Width: null, Height: null, ViewDirection: null, ViewTarget: null } });
    internal static DomainException Invalid(string path, string message, string code = "INVALID_ARGUMENT") => new(code, path + ": " + message, false, new { path });
    internal static void Shape(JsonElement value, string path, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(path, "Expected object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal)) throw Invalid(path + "." + property.Name, "Unknown or duplicate field.");
    }
    internal static JsonElement Required(JsonElement value, string name, string path) => value.TryGetProperty(name, out var item) ? item : throw Invalid(path + "." + name, "Required field is missing.");
    internal static string Text(JsonElement value, string name, string path, int max = int.MaxValue, bool empty = false)
    {
        var item = Required(value, name, path);
        if (item.ValueKind != JsonValueKind.String) throw Invalid(path + "." + name, "Expected string.");
        string text;
        try { text = item.GetString()!; }
        catch (InvalidOperationException) { throw Invalid(path + "." + name, "Invalid Unicode string."); }
        if ((!empty && text.Length == 0) || text.Length > max || text.Any(char.IsControl))
            throw Invalid(path + "." + name, "Expected a bounded string without control characters.");
        return text;
    }
    public static bool IsPlainAttribute(string text) => text.Length <= 4096 && !text.Any(char.IsControl) &&
        !text.Contains("%<", StringComparison.Ordinal) && !text.Contains("%%", StringComparison.Ordinal);
    public static bool IsOrdinaryLayerName(string name) => name.Length is > 0 and <= 255 && name == name.Trim() &&
        !name.Any(char.IsControl) && !name.Any(c => "<>/\\\":;?*|=,`".Contains(c)) &&
        !string.Equals(name, "0", StringComparison.OrdinalIgnoreCase) && !string.Equals(name, "Defpoints", StringComparison.OrdinalIgnoreCase);
    private static LayerChanges Properties(JsonElement input, string path)
    {
        Shape(input, path, "color_aci", "linetype", "lineweight_hundredth_mm", "is_off", "is_frozen", "is_locked", "is_plottable", "color_rgb", "transparency", "description");
        if (!input.EnumerateObject().Any()) throw Invalid(path, "At least one property is required.");
        int? Integer(string name)
        {
            if (!input.TryGetProperty(name, out var value)) return null;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number)) throw Invalid(path + "." + name, "Expected integer.");
            return number;
        }
        bool? Boolean(string name)
        {
            if (!input.TryGetProperty(name, out var value)) return null;
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(path + "." + name, "Expected boolean.");
            return value.GetBoolean();
        }
        int? aci = Integer("color_aci"), weight = Integer("lineweight_hundredth_mm");
        if (aci is < 1 or > 255) throw Invalid(path + ".color_aci", "A layer requires explicit ACI 1–255.");
        if (weight.HasValue && !LayerLineweights.Contains(weight.Value)) throw Invalid(path + ".lineweight_hundredth_mm", "Use Default (-3) or a supported explicit lineweight; no ByLayer/ByBlock.");
        string? linetype = input.TryGetProperty("linetype", out _) ? Text(input, "linetype", path, 255) : null;
        if (string.Equals(linetype, "ByLayer", StringComparison.OrdinalIgnoreCase) || string.Equals(linetype, "ByBlock", StringComparison.OrdinalIgnoreCase))
            throw Invalid(path + ".linetype", "A layer requires a concrete existing linetype.");
        var appearance = AppearanceValidation.Parse(input, path, layer: true);
        return new(aci, linetype, weight, Boolean("is_off"), Boolean("is_frozen"), Boolean("is_locked"), Boolean("is_plottable"), appearance.ColorRgb, appearance.Transparency, appearance.Description);
    }
    public static StructureWriteArguments Parse(JsonElement input)
    {
        Shape(input, "$", "bridge_instance_id", "document_id", "expected_revision", "operation_id", "units", "operations", "target_space");
        string? targetSpace = WriteSpace.Parse(input) == "paper" ? "paper" : null;
        if (Encoding.UTF8.GetByteCount(input.GetRawText()) > Wire.MaxRequestBytes - 1024) throw Invalid("$", "Request exceeds IPC budget.");
        string Id(string name)
        {
            if (!Guid.TryParseExact(Text(input, name, "$", 36), "D", out var id) || id == Guid.Empty) throw Invalid("$." + name, "Expected nonempty UUID.");
            return id.ToString("D");
        }
        string bridge = Id("bridge_instance_id"), document = Id("document_id"), operation = Id("operation_id");
        string revision = Text(input, "expected_revision", "$", 20), units = Text(input, "units", "$", 32);
        if (revision.Any(c => c is < '0' or > '9') || !ulong.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) throw Invalid("$.expected_revision", "Expected decimal uint64 string.");
        if (units != "Millimeters") throw Invalid("$.units", "Explicit Millimeters required.", "UNSUPPORTED_UNITS");
        var array = Required(input, "operations", "$");
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() < 1) throw Invalid("$.operations", "Expected a nonempty operation group.");
        var edits = new List<StructureEdit>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var texts = new Dictionary<string, (string Block, string Text)>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            string path = "$.operations[" + edits.Count + "]";
            if (item.ValueKind != JsonValueKind.Object) throw Invalid(path, "Expected object.");
            string kind = Text(item, "operation", path);
            switch (kind)
            {
                case var nativeStyleKind when NativeStyleLifecycleValidation352.Handles(nativeStyleKind): edits.Add(NativeStyleLifecycleValidation352.Parse(item, path)); break;
                case "rename_layer": edits.Add(LayerRenameValidation.Parse(item, path)); break;
                case "rename_block_definition": edits.Add(BlockDefinitionRenameValidation349.Parse(item, path)); break;
                case "set_block_definition_geometry": edits.Add(BlockDefinitionGeometryValidation388.Parse(item, path)); break;
                case "create_group": case "set_group": case "dissolve_group": edits.Add(DrawingGroups431.Parse(item, path)); break;
                case "create_block_from_objects": edits.Add(BlockFromObjectsValidation407.Parse(item, path)); break;
                case var libraryKind when LibraryBlockDwgValidation.Handles(libraryKind): edits.Add(LibraryBlockDwgValidation.Parse(item, path)); break;
                case "set_block_definition_text": edits.Add(BlockDefinitionTextValidation391.Parse(item, path)); break;
                case "set_block_definition_members": edits.Add(BlockDefinitionMembersValidation392.Parse(item, path)); break;
                case "create_layer":
                    Shape(item, path, "operation", "name", "properties");
                    string name = Text(item, "name", path, 255);
                    if (!IsOrdinaryLayerName(name)) throw Invalid(path + ".name", "Expected an ordinary layer name; special/dependent names are excluded.");
                    if (!names.Add(name)) throw Invalid(path + ".name", "A layer name is created more than once, ignoring case.", "INVALID_OPERATION_ORDER");
                    var supplied = item.TryGetProperty("properties", out var props) ? Properties(props, path + ".properties") : new LayerChanges(null, null, null, null, null, null, null);
                    if (supplied.IsOff == true || supplied.IsFrozen == true || supplied.IsLocked == true) throw Invalid(path + ".properties", "New layers must be visible, thawed and unlocked.");
                    edits.Add(new CreateLayerEdit(name, new(supplied.ColorRgb == null ? supplied.ColorAci ?? 7 : null, supplied.Linetype ?? "Continuous", supplied.LineweightHundredthMm ?? -3, false, false, false, supplied.IsPlottable ?? true, supplied.ColorRgb, supplied.Transparency, supplied.Description)));
                    break;
                case "set_layer_properties":
                    Shape(item, path, "operation", "handle", "properties");
                    edits.Add(new SetLayerEdit(Validation.NormalizeHandle(Text(item, "handle", path, 16)), Properties(Required(item, "properties", path), path + ".properties")));
                    break;
                case "set_current_layer":
                    Shape(item, path, "operation", "handle", "expected_current_handle");
                    edits.Add(new SetCurrentLayerEdit(Validation.NormalizeHandle(Text(item, "handle", path, 16)),
                        Validation.NormalizeHandle(Text(item, "expected_current_handle", path, 16))));
                    break;
                case "set_attribute_text":
                    Shape(item, path, "operation", "block_handle", "attribute_handle", "expected_text", "text", "content_type", "owner_definition_handle", "expected_definition_token");
                    string contentType = item.TryGetProperty("content_type", out _) ? Text(item, "content_type", path, 16) : "single_line";
                    if (contentType is not ("single_line" or "mtext")) throw Invalid(path + ".content_type", "Use single_line or mtext matching the observed native attribute.");
                    string block = Validation.NormalizeHandle(Text(item, "block_handle", path, 16));
                    string attribute = Validation.NormalizeHandle(Text(item, "attribute_handle", path, 16));
                    int contentBudget = contentType == "mtext" ? 8192 : 4096;
                    string expected = Text(item, "expected_text", path, contentBudget, true), replacement = Text(item, "text", path, contentBudget, true);
                    if (block == attribute) throw Invalid(path, "Block and attribute handles must differ.");
                    if (contentType == "single_line" ? !IsPlainAttribute(expected) || !IsPlainAttribute(replacement) : !MTextEditContent.IsCanonical(expected) || !MTextEditContent.IsCanonical(replacement))
                        throw Invalid(path, "single_line uses plain text; mtext uses canonical literal native Contents with paragraph/escape encoding. Fields and inline formatting need their owning content operation.");
                    if (texts.TryGetValue(attribute, out var previous) && (previous.Block != block || previous.Text != expected))
                        throw Invalid(path, "Repeated attribute edits must address the same insertion and expect the preceding replacement.", "INVALID_OPERATION_ORDER");
                    texts[attribute] = (block, replacement);
                    string? ownerDefinition = null, definitionToken = null;
                    if (item.TryGetProperty("owner_definition_handle", out _) || item.TryGetProperty("expected_definition_token", out _))
                    {
                        ownerDefinition = Validation.NormalizeHandle(Text(item, "owner_definition_handle", path, 16));
                        definitionToken = Text(item, "expected_definition_token", path, 64);
                        if (definitionToken.Length != 64 || definitionToken.Any(c => !"0123456789ABCDEF".Contains(c))) throw Invalid(path + ".expected_definition_token", "Read the exact current inspect_block nested_attribute_editing/definition_membership token.");
                        if (ownerDefinition == block || ownerDefinition == attribute) throw Invalid(path, "Definition, nested insertion and owned attribute handles must differ.");
                    }
                    edits.Add(new SetAttributeEdit(block, attribute, expected, replacement, contentType, ownerDefinition, definitionToken));
                    break;
                case var referenceOperation when ExternalReferenceValidation385.Handles(referenceOperation): edits.Add(ExternalReferenceValidation385.Parse(item, path)); break;
                case "define_block": edits.Add(BlockWriteValidation.ParseDefine(item, path)); break;
                case "insert_block": edits.Add(BlockWriteValidation.ParseInsert(item, path)); break;
                case "insert_block_array":
                case "set_block_array_properties": edits.Add(NativeBlockArrayValidation384.Parse(item, path)); break;
                case "set_text_style_font": edits.Add(StyleWriteValidation.ParseFont(item, path)); break;
                case "create_text_style": edits.Add(StyleWriteValidation.ParseCreate(item, path)); break;
                case "set_text_style_properties": edits.Add(StyleWriteValidation.ParseSet(item, path)); break;
                case "create_dimension_style": edits.Add(DimensionStyleWriteValidation.ParseCreate(item, path)); break;
                case "set_dimension_style_properties": edits.Add(DimensionStyleWriteValidation.ParseSet(item, path)); break;
                case "create_linetype": edits.Add(LinetypeWriteValidation.ParseCreate(item, path)); break;
                case "set_viewport_clipping": edits.Add(ViewportClippingValidation358.Parse(item, path)); break;
                case "set_viewport_view": edits.Add(ViewportWriteValidation.Parse(item, path)); break;
                case "create_layout": edits.Add(LayoutCreateValidation.Parse(item, path)); break;
                case "rename_layout":
                case "set_layout_properties": edits.Add(LayoutEditValidation.Parse(item, path)); break;
                case "create_drawing_views": edits.Add(DrawingViewCreateValidation.Parse(item, path)); break;
                case "create_section_view": edits.Add(SectionViewValidation.Parse(item, path)); break;
                case "set_section_view_cut": edits.Add(SectionCutValidation351.Parse(item, path)); break;
                case "create_viewport": edits.Add(ViewportCreateValidation.Parse(item, path)); break;
                case var tableStyleKind when TableStyleValidation337.Handles(tableStyleKind): edits.Add(TableStyleValidation337.Parse(item, path)); break;
                case var leaderStyleKind when MLeaderStyleValidation337.Operations.Contains(leaderStyleKind, StringComparer.Ordinal): edits.Add(MLeaderStyleValidation337.Parse(item, path)); break;
                case var viewportLifecycleKind when ViewportLifecycleValidation.Handles(viewportLifecycleKind): edits.Add(ViewportLifecycleValidation.Parse(item, path)); break;
                case var lifecycleKind when StructureLifecycleValidation.Handles(lifecycleKind): edits.Add(StructureLifecycleValidation.Parse(item, path)); break;
                case var lifecycleKind when DrawingViewLifecycleValidation345.Handles(lifecycleKind): edits.Add(DrawingViewLifecycleValidation345.Parse(item, path)); break;
                case var drawingViewKind when DrawingViewEditValidation.Handles(drawingViewKind): edits.Add(DrawingViewEditValidation.Parse(item, path)); break;
                default: throw Invalid(path + ".operation", "Supported: set_text_style_font, set_text_style_properties, set_dimension_style_properties, create_layer, set_layer_properties, set_current_layer, rename_layer, rename_block_definition, set_block_definition_geometry, create_block_from_objects, import_block_dwg, redefine_block_from_dwg, set_block_definition_text, set_block_definition_members, set_attribute_text, define_block, insert_block, insert_block_array, set_block_array_properties, create_text_style, create_dimension_style, create_linetype, set_viewport_clipping, set_viewport_view, create_layout, rename_layout, set_layout_properties, create_viewport, create_drawing_views, create_section_view, set_section_view_cut, native current style and safe record lifecycle operations.");
            }
        }
        if (edits.Any(e => e is SetTextStyleFontEdit) && edits.Count != 1) throw Invalid("operations", "Change one text style font in a separate request before creating or editing annotations.");
        if (edits.Any(e => e is SetCurrentLayerEdit) && edits.Count != 1)
            throw Invalid("operations", "Set the current layer in a separate request, using the current handle from get_structure layers.", "INVALID_OPERATION_ORDER");
        if (edits.Any(e => e is SetTextStylePropertiesEdit or SetDimensionStylePropertiesEdit) && edits.Count != 1)
            throw Invalid("operations", "Edit one existing style in a separate request before editing its dependent annotations.", "INVALID_OPERATION_ORDER");
        ExternalReferenceValidation385.Batch(edits);
        LayerRenameValidation.Batch(edits);
        BlockDefinitionRenameValidation349.Batch(edits);
        BlockDefinitionGeometryValidation388.Batch(edits);
        BlockFromObjectsValidation407.Batch(edits);
        LibraryBlockDwgValidation.Batch(edits);
        DrawingGroups431.Batch(edits);
        BlockDefinitionTextValidation391.Batch(edits);
        BlockDefinitionMembersValidation392.Batch(edits);
        if (edits.Any(e => e is SetAttributeEdit { OwnerDefinitionHandle: not null }))
        {
            var nested = edits.OfType<SetAttributeEdit>().ToArray();
            if (nested.Length != edits.Count || nested.Any(e => e.OwnerDefinitionHandle != nested[0].OwnerDefinitionHandle || e.ExpectedDefinitionToken != nested[0].ExpectedDefinitionToken) ||
                nested.Select(e => e.AttributeHandle).Distinct(StringComparer.Ordinal).Count() != nested.Length)
                throw Invalid("operations", "Edit distinct nested attributes of one shared owner definition with one fresh definition token in a separate request.", "INVALID_OPERATION_ORDER");
        }
        NativeBlockArrayValidation384.Batch(edits);
        StructureLifecycleValidation.Batch(edits);
        DrawingViewEditValidation.Batch(edits);
        DrawingViewLifecycleValidation345.Batch(edits);
        ViewportLifecycleValidation.Batch(edits);
        TableStyleValidation337.Batch(edits);
        MLeaderStyleValidation337.Batch(edits);
        NativeStyleLifecycleValidation352.Batch(edits);
        ViewportClippingValidation358.Batch(edits);
        if (edits.Count > 0 && edits.All(edit => edit is ViewportLifecycleEdit))
            return new(bridge, document, number.ToString(CultureInfo.InvariantCulture), operation, units, edits.ToArray(), targetSpace);
        // A validated nonempty view-only group creates no new definitions.
        // Unrelated creator budgets cannot cap a native aligned view tree.
        if (edits.Count > 0 && edits.All(edit => edit is SetDrawingViewPropertiesEdit))
            return new(bridge, document, number.ToString(CultureInfo.InvariantCulture), operation, units, edits.ToArray(), targetSpace);
        LayoutEditValidation.Batch(edits);
        DrawingViewCreateValidation.ValidateBatch(edits);
        DrawingViewCreateValidation.ValidateTargetSpace(edits, targetSpace ?? "model");
        SectionViewValidation.ValidateBatch(edits);
        SectionCutValidation351.Batch(edits);
        ViewportCreateValidation.ValidateBatch(edits);
        return new(bridge, document, number.ToString(CultureInfo.InvariantCulture), operation, units, edits.ToArray(), targetSpace);
    }
}

// Pure policy used by the native preflight to model ordered permission changes.
public sealed record LayerWriteState(string Name, bool Current, bool Dependent, bool Off, bool Frozen, bool Locked)
{
    public LayerWriteState Change(LayerChanges changes)
    {
        if (Dependent || Name.Contains('|')) throw new DomainException("LAYER_UNAVAILABLE", "Dependent layers cannot be edited by this capability.");
        if (Current && (changes.IsOff == true || changes.IsFrozen == true || changes.IsLocked == true))
            throw new DomainException("CURRENT_LAYER_PROTECTED", "The current layer cannot be switched off, frozen or locked by this capability.");
        return this with { Off = changes.IsOff ?? Off, Frozen = changes.IsFrozen ?? Frozen, Locked = changes.IsLocked ?? Locked };
    }
    public void RequireAttributeWrite()
    {
        if (Dependent || Off || Frozen || Locked) throw new DomainException("LAYER_UNAVAILABLE", "Attribute and insertion layers must be visible, thawed, unlocked and independent at this operation.");
    }
}

