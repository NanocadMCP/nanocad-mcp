using System.Text;

namespace NanoCAD.Contracts;

// Dimension-style properties for get_structure, since candidate 0.9. No native calls.
public sealed record DimensionVariables
{
    public int dimadec { get; init; }
    public bool dimalt { get; init; }
    public int dimaltd { get; init; }
    public double dimaltf { get; init; }
    public double dimaltrnd { get; init; }
    public int dimalttd { get; init; }
    public int dimalttz { get; init; }
    public int dimaltu { get; init; }
    public int dimaltz { get; init; }
    public string dimapost { get; init; } = "";
    public int dimarcsym { get; init; }
    public int dimatfit { get; init; }
    public int dimaunit { get; init; }
    public int dimazin { get; init; }
    public double dimcen { get; init; }
    public int dimdec { get; init; }
    public double dimdle { get; init; }
    public double dimdli { get; init; }
    public int dimdsep { get; init; }
    public double dimexe { get; init; }
    public double dimexo { get; init; }
    public int dimfrac { get; init; }
    public double dimfxlen { get; init; }
    public bool dimfxlenon { get; init; }
    public double dimgap { get; init; }
    public double dimjogang { get; init; }
    public int dimjust { get; init; }
    public double dimlfac { get; init; }
    public bool dimlim { get; init; }
    public int dimlunit { get; init; }
    public string dimpost { get; init; } = "";
    public double dimrnd { get; init; }
    public bool dimsah { get; init; }
    public bool dimsd1 { get; init; }
    public bool dimsd2 { get; init; }
    public bool dimse1 { get; init; }
    public bool dimse2 { get; init; }
    public bool dimsoxd { get; init; }
    public int dimtad { get; init; }
    public int dimtdec { get; init; }
    public double dimtfac { get; init; }
    public int dimtfill { get; init; }
    public bool dimtih { get; init; }
    public bool dimtix { get; init; }
    public double dimtm { get; init; }
    public int dimtmove { get; init; }
    public bool dimtofl { get; init; }
    public bool dimtoh { get; init; }
    public bool dimtol { get; init; }
    public int dimtolj { get; init; }
    public double dimtp { get; init; }
    public double dimtsz { get; init; }
    public double dimtvp { get; init; }
    public int dimtzin { get; init; }
    public bool dimupt { get; init; }
    public int dimzin { get; init; }
}

public sealed record DimensionDependency(string target_kind, string state, EntityRef? @ref, string? name, string? reason);
public sealed record DimensionReferences(DimensionDependency arrow, DimensionDependency first_arrow, DimensionDependency second_arrow,
    DimensionDependency leader_arrow, DimensionDependency extension1_linetype, DimensionDependency extension2_linetype,
    DimensionDependency dimension_linetype, DimensionDependency text_style);
public sealed record DimensionRgb(byte red, byte green, byte blue);
public sealed record DimensionColor(string method, int native_value, short? aci, DimensionRgb? rgb,
    bool has_color_name, string color_name, string book_name);
public sealed record DimensionColors(DimensionColor dimension, DimensionColor extension, DimensionColor text, DimensionColor text_fill);
public sealed record DimensionLineweight(string mode, int native_value);
public sealed record DimensionLineweights(DimensionLineweight dimension, DimensionLineweight extension);
public sealed record DimensionStyleProperties(bool is_dependent, double dimension_scale, double text_height, double arrow_size,
    DimensionVariables variables, DimensionReferences references, DimensionColors colors, DimensionLineweights lineweights,
    string annotative_state, int annotative_native_value, bool has_extension_dictionary, bool has_fields, bool has_xdata,
    bool family_name_candidate);

public static class DimensionStyleMetadata
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static readonly string[] DependencyReasons = { "invalid_id", "erased", "wrong_type", "wrong_owner", "read_failed" };
    public static readonly int[] ExplicitLineweights = { 0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211 };
    private static void Fail(string message) => throw new DomainException("OBJECT_READ_FAILED", message);
    public static string Text(string? value, int limit)
    {
        if (value is null) throw new DomainException("OBJECT_READ_FAILED", "Missing dimension style text.");
        if (value.Length > limit) throw new DomainException("ENTITY_TOO_LARGE", "Dimension style text exceeds its UTF-16 limit; it was not truncated.");
        try { StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException) { Fail("Dimension style text contains invalid Unicode."); }
        return value;
    }
    public static string Annotative(int value) => value switch { 0 => "true", 1 => "false", 2 => "not_applicable", _ => "unknown" };
    public static string ColorMethod(int value) => value switch {
        192 => "by_layer", 193 => "by_block", 194 => "true_color", 195 => "aci", 196 => "by_pen",
        197 => "foreground", 198 => "layer_off", 199 => "layer_frozen", 200 => "none", _ => "unknown" };
    public static string WeightMode(int value) => value switch {
        -1 => "by_layer", -2 => "by_block", -3 => "default", _ => ExplicitLineweights.Contains(value) ? "explicit" : "unknown" };
    public static bool FamilyNameCandidate(string name)
    {
        Text(name, 16384);
        int suffix = name.LastIndexOf('$');
        return suffix > 0 && suffix < name.Length - 1 && name[(suffix + 1)..].All(c => c is >= '0' and <= '9');
    }
    public static DimensionStyleProperties Validate(string name, EntityRef owner, DimensionStyleProperties value)
    {
        ValidateIdentity(owner);
        if (value.variables is null || value.references is null || value.colors is null || value.lineweights is null)
            throw new DomainException("OBJECT_READ_FAILED", "Missing dimension style metadata group.");
        var v = value.variables;
        foreach (double number in new[] { value.dimension_scale, value.text_height, value.arrow_size,
            v.dimaltf, v.dimaltrnd, v.dimcen, v.dimdle, v.dimdli, v.dimexe, v.dimexo, v.dimfxlen, v.dimgap,
            v.dimjogang, v.dimlfac, v.dimrnd, v.dimtfac, v.dimtm, v.dimtp, v.dimtsz, v.dimtvp })
            if (!double.IsFinite(number)) Fail("Dimension style contains a non-finite value.");
        Text(v.dimpost, 4096); Text(v.dimapost, 4096);
        if (v.dimdsep is < 0 or > 65535) Fail("DIMDSEP must be a UTF-16 code unit.");
        var r = value.references;
        ValidateDependency(owner, r.arrow, "block"); ValidateDependency(owner, r.first_arrow, "block");
        ValidateDependency(owner, r.second_arrow, "block"); ValidateDependency(owner, r.leader_arrow, "block");
        ValidateDependency(owner, r.extension1_linetype, "linetype"); ValidateDependency(owner, r.extension2_linetype, "linetype");
        ValidateDependency(owner, r.dimension_linetype, "linetype"); ValidateDependency(owner, r.text_style, "text_style");
        foreach (var color in new[] { value.colors.dimension, value.colors.extension, value.colors.text, value.colors.text_fill })
        {
            if (color is null) throw new DomainException("OBJECT_READ_FAILED", "Missing dimension color.");
            if (color.method != ColorMethod(color.native_value) || (color.aci != null) != (color.native_value == 195) ||
                (color.rgb != null) != (color.native_value == 194)) Fail("Inconsistent dimension color representation.");
            Text(color.color_name, 1024); Text(color.book_name, 1024);
        }
        foreach (var weight in new[] { value.lineweights.dimension, value.lineweights.extension })
            if (weight is null || weight.mode != WeightMode(weight.native_value)) Fail("Inconsistent dimension lineweight representation.");
        if (value.annotative_state != Annotative(value.annotative_native_value) || value.family_name_candidate != FamilyNameCandidate(name))
            Fail("Inconsistent dimension style metadata label.");
        return value;
    }
    private static void ValidateIdentity(EntityRef value)
    {
        if (value is null || !Guid.TryParseExact(value.bridge_instance_id, "D", out _) || !Guid.TryParseExact(value.document_id, "D", out _))
            throw new DomainException("OBJECT_READ_FAILED", "Invalid dimension dependency lifetime.");
        if (value.handle is null) throw new DomainException("OBJECT_READ_FAILED", "Missing dimension dependency handle.");
        try { if (Validation.NormalizeHandle(value.handle) != value.handle) Fail("Noncanonical dimension dependency handle."); }
        catch (DomainException) { Fail("Invalid dimension dependency handle."); }
    }
    private static void ValidateDependency(EntityRef owner, DimensionDependency value, string kind)
    {
        if (value is null) throw new DomainException("OBJECT_READ_FAILED", "Missing dimension style dependency.");
        if (value.target_kind != kind) Fail("Incorrect dimension dependency table kind.");
        if (value.state == "resolved")
        {
            if (value.@ref is null || value.name is null || value.reason != null) Fail("Incomplete resolved dimension dependency.");
            ValidateIdentity(value.@ref!); Text(value.name, 16384);
            if (value.@ref!.bridge_instance_id != owner.bridge_instance_id || value.@ref.document_id != owner.document_id)
                Fail("Dimension dependency belongs to another document lifetime.");
        }
        else if (value.state is "null" or "unavailable")
        {
            if (value.@ref != null || value.name != null || (value.state == "null" ? value.reason != null : value.reason is null || !DependencyReasons.Contains(value.reason)))
                Fail("Unavailable/null dependency cannot claim a resolved identity.");
        }
        else Fail("Unknown dimension dependency state.");
    }
}
