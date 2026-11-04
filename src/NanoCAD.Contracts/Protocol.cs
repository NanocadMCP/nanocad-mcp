using System.Globalization;
using System.Text.Json;

namespace NanoCAD.Contracts;

public static class Wire
{
    public const string Version = ComponentVersions.Protocol;
    public const string ProductVersion = ComponentVersions.Bridge;
    public const int MaxRequestBytes = 256 * 1024;
    public const int MaxResponseBytes = 1024 * 1024;
    // A result is also embedded as JSON text in MCP. Reserve room for escaping/wrappers.
    public const int PayloadBudget = 240 * 1024;
    public static readonly JsonSerializerOptions Json = new() { MaxDepth = 32 };
    public static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, Json);
    public static readonly string[] ReadOperations = { "get_status", "get_operation_result", "get_document", "get_selection", "query_objects", "inspect_objects", "get_structure", "inspect_block", "get_documents", "analyze_geometry", "inspect_model_history", "get_model_variables", "inspect_sketch_constraints", "get_drawing_views", "inspect_solid_topology", "inspect_constraints", "inspect_point_cloud", "analyze_point_cloud" };
    public static readonly string[] WriteOperations = { "create_entities", "modify_entities", "manage_document", "modify_structure", "set_model_parameter", "set_view" };
    public static readonly string[] Operations = ReadOperations.Concat(WriteOperations).ToArray();
    public static string? ValidateBridge(string operation, JsonElement input) => operation switch
    {
        "capture_window" => VisualCaptureBridgeArguments.Parse(input).BridgeId,
        WritePreviewRequest.Route => WritePreviewRequest.Parse(input).BridgeId,
        _ => WriteOperations.Contains(operation) ? WriteValidation.Parse(operation, input).BridgeId : Validation.Parse(operation, input).BridgeId
    };
}

public sealed record Request(string protocolVersion, string requestId, string operation, int timeoutMs, JsonElement arguments);
public sealed record ReadContext(string bridge_instance_id, string document_id, string revision, string captured_at_utc)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? write_revision => ScopedWriteRevision.Format(this);
}
public sealed record DomainError(string code, string message, bool retryable = false, object? details = null);
public sealed record Result(string contract_version, string request_id, bool success, ReadContext? context,
    object? data, DomainError? error, string[] warnings)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public WriteReceipt432? receipt { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public WriteStateObservation? write_state { get; init; }
    public static Result Ok(string id, object data, ReadContext? context = null, params string[] warnings) =>
        new(Wire.Version, id, true, context, data, null, warnings);
    public static Result Fail(string id, DomainError error, ReadContext? context = null) =>
        new(Wire.Version, id, false, context, null, error, Array.Empty<string>());
}
public sealed class DomainException : Exception
{
    public DomainError Error { get; }
    public DomainException(string code, string message, bool retryable = false, object? details = null) : base(message) =>
        Error = new(code, message, retryable, details);
}

public sealed record EntityRef(string bridge_instance_id, string document_id, string handle);
public sealed record LayerDto(string name, string? handle);
public sealed record EntitySummary(EntityRef @ref, string type, string runtime_type, LayerDto? layer, string space, string detail_support);
public sealed record EntityDetail(EntityRef @ref, string type, string runtime_type, LayerDto? layer, string space,
    string detail_support, string coordinate_system, object geometry, object properties, string[] warnings);
public sealed record ItemResult(string handle, bool success, EntityDetail? entity, DomainError? error);
public sealed record Page(EntitySummary[] items, int? total_count, int returned_count, bool complete, string? next_cursor, string? selection_revision);
public sealed record Descriptor(string protocol_version, string bridge_instance_id, string pipe_name, int process_id,
    long process_start_utc_ticks, int session_id, string host_version, string bridge_version, string created_at_utc);

public sealed record Arguments(string? BridgeId, string? DocumentId, string? ExpectedRevision,
    int PageSize, string? Cursor, string[]? Types, string[]? Layers, string[]? Handles,
    string? Section = null, string? BlockHandle = null, int MaxDepth = 4, int MaxNodes = int.MaxValue, string? ModelObjectId = null, bool IncludeView = false,
    string[]? SketchGeometryIds = null, bool IncludeVariables = false, SketchPlaneSpec? Workplane = null,
    string? GeometryViewHandle = null, bool IncludeNativeModelFaces = false, string ReadSpace = "model", bool IncludeNativeModelEdges = false,
    int GeometryStartIndex = 0, int GeometryPageSize = 100,
    bool IncludeDocumentProfiles = false, string? DocumentProfileCursor = null, string[]? ModelProfileIds = null, string[]? ModelProfileRegionIds = null, string[]? ModelExtrusionExtentIds = null, bool IncludeDocumentDatumPlanes = false, string? DocumentDatumCursor = null, string[]? ModelDatumPlaneIds = null, string[]? ModelDatumPlaneRevisionIds = null, string? DrawingViewSourceIdentityHandle = null, string[]? ModelProfileElementIds = null, bool IncludeDocumentSketches = false, string? DocumentSketchCursor = null, string[]? ModelSketchIds = null)
{
    public string[]? ModelFeatureDeletionIds { get; init; }
    public bool IncludeGeometrySources { get; init; }
    public string[]? SketchConstraintParameterIds { get; init; }
    public QueryBounds407? QueryBounds { get; init; }
    public bool SummaryOnly { get; init; }
    public QueryText407? QueryText { get; init; }
    public QueryBlock407? QueryBlock { get; init; }
    public string[]? DetailSections { get; init; }
    public bool EraseAssessment { get; init; }
    public string FilterKey(string operation) => JsonSerializer.Serialize(new { operation, Types, Layers, Section, ReadSpace, QueryBounds, QueryText, QueryBlock }, Wire.Json);
}

public static class Validation
{
    public static string NormalizeHandle(string text)
    {
        if (text.Length is < 1 or > 16 || text.Any(c => !Uri.IsHexDigit(c)) ||
            !ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) || value == 0)
            throw new DomainException("INVALID_HANDLE", "Handle must contain 1–16 hexadecimal digits, nonzero, without 0x.");
        return value.ToString("X", CultureInfo.InvariantCulture);
    }

    public static Arguments Parse(string operation, JsonElement input)
    {
        if (!Wire.ReadOperations.Contains(operation)) throw new DomainException("INVALID_ARGUMENT", "Operation is not in the read allowlist.");
        if (operation == "get_operation_result")
        {
            var read = OperationResultArguments432.Parse(input);
            return new(read.BridgeId, null, null, read.PageSize, read.Cursor, null, null, null);
        }
        if (operation == "analyze_geometry") return GeometryAnalysisValidation.Parse(input).Read;
        if (operation == "inspect_model_history") return ModelHistoryValidation.Parse(input);
        if (operation == "get_model_variables") return ModelVariablesValidation.Parse(input);
        if (operation == "inspect_constraints") return DocumentConstraintsValidation.Parse(input);
        if (operation == "inspect_sketch_constraints") return SketchConstraintsValidation.Parse(input);
        if (operation == "get_drawing_views") return DrawingViewsValidation.Parse(input);
        if (operation == "inspect_solid_topology") return SolidTopologyValidation.Parse(input);
        if (operation == "inspect_point_cloud") return PointCloudInspectValidation.Parse(input).Read;
        if (operation == "analyze_point_cloud") return PointCloudAnalysisValidation.Parse(input).Read;
        if (operation is "get_structure" or "inspect_block") return StructureValidation.Parse(operation, input);
        if (input.ValueKind != JsonValueKind.Object) throw InputError.Create("$", "Expected an object.");
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "bridge_instance_id" };
        if (operation == "get_document") allowed.Add("include_view");
        if (operation is "get_selection" or "query_objects" or "inspect_objects")
            allowed.UnionWith(new[] { "document_id", "expected_revision" });
        if (operation is "get_selection" or "query_objects") allowed.UnionWith(new[] { "page_size", "cursor" });
        if (operation == "query_objects") allowed.UnionWith(new[] { "space", "types", "layers", "bounds", "text", "block_definition", "summary_only" });
        if (operation == "inspect_objects") allowed.UnionWith(new[] { "handles", "sections", "erase_assessment" });
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in input.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name))
                throw InputError.Create("$." + property.Name, "Unknown or duplicate field.");
        string? ReadString(string name, int max, bool required = false)
        {
            if (!input.TryGetProperty(name, out var item))
            {
                if (required) throw InputError.Create("$." + name, "Required field is missing.");
                return null;
            }
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } s || s.Length > max)
                throw InputError.Create("$." + name, "Expected a nonempty string.", new { max_length = max });
            return s;
        }
        string? ReadId(string name, bool required = false)
        {
            var s = ReadString(name, 36, required);
            if (s != null && !Guid.TryParseExact(s, "D", out _)) throw InputError.Create("$." + name, "Expected a UUID.");
            return s?.ToLowerInvariant();
        }
        string[]? ReadArray(string name, int? maxCount, int maxLength)
        {
            if (!input.TryGetProperty(name, out var array)) return null;
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() < 1 || maxCount.HasValue && array.GetArrayLength() > maxCount.Value)
                throw new DomainException("INVALID_ARGUMENT", $"{name} requires a nonempty array" + (maxCount.HasValue ? $" of at most {maxCount} values." : "."));
            return array.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s && s.Length <= maxLength
                ? s : throw new DomainException("INVALID_ARGUMENT", $"Invalid value in {name}.")).ToArray();
        }
        var bridgeId = ReadId("bridge_instance_id");
        var documentId = ReadId("document_id", operation is "get_selection" or "query_objects" or "inspect_objects");
        var revision = ReadString("expected_revision", 20);
        if (revision != null && (revision.Any(c => c is < '0' or > '9') || !ulong.TryParse(revision, out _)))
            throw new DomainException("INVALID_ARGUMENT", "expected_revision must be a decimal uint64 string.");
        revision = revision == null ? null : ulong.Parse(revision, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        bool summaryOnly = false;
        if (input.TryGetProperty("summary_only", out var summary))
        {
            if (summary.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new DomainException("INVALID_ARGUMENT", "summary_only must be a boolean.", false, new { path = "$.summary_only" });
            summaryOnly = summary.GetBoolean();
            foreach (var field in new[] { "page_size", "cursor" })
                if (summaryOnly && input.TryGetProperty(field, out _))
                    throw new DomainException("INVALID_ARGUMENT", $"{field} cannot be used with summary_only.", false, new { path = "$." + field });
        }
        int pageSize = 100;
        if (input.TryGetProperty("page_size", out var page) && (page.ValueKind != JsonValueKind.Number || !page.TryGetInt32(out pageSize) || pageSize < 1))
            throw InputError.Create("$.page_size", "Expected a positive integer.", new { minimum = 1, maximum = int.MaxValue });
        var space = ReadString("space", 10);
        if (space != null && space is not ("model" or "paper")) throw InputError.Create("$.space", "Paper queries require an active sheet.", new[] { "model", "paper" });
        var types = ReadArray("types", null, 32);
        if (types != null && types.Any(t => !new[] { "Line", "Circle", "Arc", "Ellipse", "Spline", "Helix", "Region", "Polyline", "Polyline2d", "Polyline3d", "Point", "Ray", "Xline", "Text", "MText", "Dimension", "Tolerance", "MLeader", "Table", "Hatch", "Solid3d", "BlockReference", "MInsertBlock", "Wipeout", "RasterImage", "DwfUnderlay", "DgnUnderlay", "PdfUnderlay", "PointCloud", "Unknown" }.Contains(t)))
            throw new DomainException("INVALID_ARGUMENT", "Unknown semantic entity type.");
        var layers = ReadArray("layers", null, 255);
        var handles = ReadArray("handles", null, 16)?.Select(NormalizeHandle).ToArray();
        if (operation == "inspect_objects" && handles == null) throw new DomainException("INVALID_ARGUMENT", "handles is required.");
        if (handles != null && handles.Distinct().Count() != handles.Length) throw new DomainException("INVALID_ARGUMENT", "Duplicate normalized handles are not allowed.");
        bool includeView = false;
        if (input.TryGetProperty("include_view", out var view))
        {
            if (view.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new DomainException("INVALID_ARGUMENT", "include_view must be a boolean.");
            includeView = view.GetBoolean();
        }
        bool eraseAssessment = false;
        if (input.TryGetProperty("erase_assessment", out var assessment))
        {
            if (assessment.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new DomainException("INVALID_ARGUMENT", "erase_assessment must be a boolean.");
            eraseAssessment = assessment.GetBoolean();
        }
        return new(bridgeId, documentId, revision, pageSize, ReadString("cursor", 2048),
            types?.Distinct().OrderBy(t => t, StringComparer.Ordinal).ToArray(),
            layers?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray(), handles, IncludeView: includeView, ReadSpace: space ?? "model")
        {
            QueryBounds = TargetedRead407.Bounds(input),
            SummaryOnly = summaryOnly,
            QueryText = TargetedRead407.TextFilter(input),
            QueryBlock = TargetedRead407.BlockFilter(input),
            DetailSections = TargetedRead407.Sections(input),
            EraseAssessment = eraseAssessment
        };
    }
}




