using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Only the new reusable defaults route uses this additional consumer snapshot.
// Resident objects belong to the transaction; GetDimstyleData owns a detached
// record after the public identity check. No dependent source geometry is edited.
internal static class CadDimensionStyleDefaults364
{
    internal sealed record Consumer(ObjectId Id, JsonElement Stable, JsonElement Effective);
    internal sealed record State(ObjectId Style, ObjectId CurrentLayer, ObjectId CurrentTextStyle, ObjectId CurrentDimensionStyle,
        JsonElement CurrentDefaults, JsonElement StyleReactors, Consumer[] Consumers, bool ConsumerInventory);
    private static string? Id(ObjectId id) => id.IsNull ? null : Validation.NormalizeHandle(id.Handle.ToString());
    private static bool Live(ObjectId id) => !id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased;
    private static void Need(bool good, string message, object? details = null)
    { if (!good) throw new DomainException("NATIVE_DIMENSION_STYLE_MISMATCH", message, false, details); }
    private static bool Exact(JsonElement a, JsonElement b) => a.GetRawText() == b.GetRawText();
    private static EntityRef Ref(EntityRef context, ObjectId id) => new(context.bridge_instance_id, context.document_id, Id(id)!);
    private static JsonElement CurrentDefaults(EntityRef context, Database db, Transaction tr, Action cancel)
    {
        cancel(); var acquired = db.GetDimstyleData();
        if (acquired == null || acquired.GetType() != typeof(DimStyleTableRecord) || !acquired.ObjectId.IsNull)
        {
            if (acquired != null) GC.SuppressFinalize(acquired); // Unproved resident aliases must never be disposed.
            throw new DomainException("UNSAFE_NATIVE_CLONE", "Current DIM defaults did not return an exact detached record.");
        }
        using var defaults = acquired;
        return Wire.Element(DimensionStyleMetadataReader.ReadDetached(defaults, context, tr, db, id => Ref(context, id), cancel, cancel));
    }
    private static Consumer CaptureDimension(Dimension opened, ObjectId style, Database db, Transaction tr, Action cancel)
    {
        cancel(); Need(CadNativeDimensionBinding357.Supported(opened), "A referring native dimension kind needs its owning adapter.", CadNativeDimensionBinding357.NativeDetails(opened));
        using var binding = CadNativeDimensionBinding357.Bind(opened); var d = (Dimension)binding.Value;
        Need(Live(d.ObjectId) && d.Database == db && d.DimensionStyle == style && Live(d.OwnerId), "A referring dimension identity/style/owner is unavailable.");
        if (tr.GetObject(d.OwnerId, OpenMode.ForRead) is not BlockTableRecord owner || owner.GetType() != typeof(BlockTableRecord) ||
            owner.Database != db || owner.OwnerId != db.BlockTableId || owner.IsDependent || owner.IsDynamicBlock ||
            owner.IsFromExternalReference || owner.IsFromOverlayReference)
            throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT", "A referring dimension requires its native owning source route.", false,
                new { dimension_handle = Id(d.ObjectId), owner_handle = Id(d.OwnerId) });
        using var reactors = d.GetPersistentReactorIds();
        Need(!d.HasFields && reactors.Count == 0 && d.Annotative != AnnotativeStates.True,
            "Associated, field-bearing or contextual dimensions require their owning route before changing inherited defaults.",
            new { dimension_handle = Id(d.ObjectId), d.HasFields, reactor_count = reactors.Count, annotative = (int)d.Annotative });
        var budget = new ModelInventoryBudget(cancel); using var xdata = d.XData; using var color = d.Color;
        var owned = NativeOwnedMetadata.Capture(d, tr, cancel);
        var stable = Wire.Element(new
        {
            handle = Id(d.ObjectId), owner = Id(d.OwnerId), runtime_type = d.GetType().FullName,
            native_binding = CadNativeDimensionBinding357.NativeDetails(d), style = Id(d.DimensionStyle),
            layer = Id(d.LayerId), d.Visible,
            color = new { method = color.ColorMethod.ToString(), color.ColorIndex, color.Red, color.Green, color.Blue,
                color.HasColorName, color.ColorName, color.BookName },
            linetype = Id(d.LinetypeId), d.LinetypeScale, lineweight = (int)d.LineWeight,
            transparency = new { d.Transparency.Alpha, d.Transparency.IsByAlpha, d.Transparency.IsByLayer, d.Transparency.IsByBlock },
            annotative = (int)d.Annotative, extension = Id(d.ExtensionDictionary),
            xdata = xdata == null ? null : NativeOwnedMetadata.Buffer(xdata, cancel, budget.Add), owned,
            definition = DimensionGeometryReader.Definition(d), normal = new[] { d.Normal.X, d.Normal.Y, d.Normal.Z }, d.Elevation,
            d.DimensionText, d.TextRotation, d.Dimtmove, d.UsingDefaultTextPosition,
            // Automatic native annotation placement may reflow. An explicit text
            // position remains exact, as do all measured anchors and stored controls.
            explicit_text_position = d.UsingDefaultTextPosition ? null : new[] { d.TextPosition.X, d.TextPosition.Y, d.TextPosition.Z },
            native_controls = CadExtendedDimensions356.Controls(d)
        });
        var effective = CadDimensionEditing341.RetainedStyleSnapshot(d, db, tr, cancel);
        budget.Add(stable); budget.Add(effective); return new(d.ObjectId, stable, effective);
    }
    internal static State Capture(ObjectId styleId, EntityRef context, Database db, Transaction tr, Action cancel, bool consumerInventory)
    {
        cancel(); Need(Live(styleId), "Native dimension style is unavailable.");
        if (tr.GetObject(styleId, OpenMode.ForRead) is not DimStyleTableRecord style || style.GetType() != typeof(DimStyleTableRecord) ||
            style.Database != db || style.OwnerId != db.DimStyleTableId || style.IsDependent)
            throw new DomainException("UNSUPPORTED_STYLE_TYPE", "Expected one exact local native dimension style.");
        var linked = new HashSet<ObjectId>(); var reactorIds = new List<string>(); var budget = new ModelInventoryBudget(cancel);
        using (var reactors = style.GetPersistentReactorIds()) foreach (ObjectId id in reactors)
        {
            cancel(); Need(Live(id), "Dimension style has an unavailable native dependency.");
            budget.Add(new { reactor_handle = Id(id) }); reactorIds.Add(Id(id)!);
            // Creation only clones/adds a detached style. Preserve the untouched
            // source's reactor membership without imposing consumer edit eligibility.
            if (!consumerInventory) continue;
            var item = tr.GetObject(id, OpenMode.ForRead);
            bool owningTable = id == style.OwnerId && id == db.DimStyleTableId && item.GetType() == typeof(DimStyleTable) && item.Database == db;
            bool consumer = item is Dimension dimension && dimension.Database == db && dimension.DimensionStyle == styleId && CadNativeDimensionBinding357.Supported(dimension);
            Need(owningTable || consumer, "Dimension style has an unclassified native dependency.",
                new { style_handle = Id(styleId), dependency_handle = Id(id), owner_handle = Id(item.OwnerId), runtime_type = item.GetType().FullName });
            if (consumer) linked.Add(id);
        }
        var consumers = new List<Consumer>(); var found = new HashSet<ObjectId>();
        if (consumerInventory) foreach (ObjectId blockId in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
        {
            cancel(); if (!Live(blockId)) continue;
            if (tr.GetObject(blockId, OpenMode.ForRead) is not BlockTableRecord block || block.GetType() != typeof(BlockTableRecord))
                throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT", "A native dimension owning block requires its dedicated adapter.");
            foreach (ObjectId entityId in block)
            {
                cancel(); if (!Live(entityId)) continue;
                if (tr.GetObject(entityId, OpenMode.ForRead) is not Dimension d || d.DimensionStyle != styleId) continue;
                Need(d.OwnerId == blockId && found.Add(entityId), "A referring dimension has inconsistent or repeated ownership.");
                var captured = CaptureDimension(d, styleId, db, tr, cancel);
                budget.Add(new { handle = Id(entityId), captured.Stable, captured.Effective }); consumers.Add(captured);
            }
        }
        Need(linked.All(found.Contains), "A style reactor names a dimension outside its complete native owning-block inventory.");
        var defaults = CurrentDefaults(context, db, tr, cancel); budget.Add(defaults);
        // Enumeration order is not a dependency. Membership and multiplicity
        // remain exact; no reactor is removed from either observation.
        reactorIds.Sort(StringComparer.Ordinal);
        consumers.Sort((a, b) => StringComparer.Ordinal.Compare(Id(a.Id), Id(b.Id)));
        return new(styleId, db.Clayer, db.Textstyle, db.Dimstyle, defaults, Wire.Element(reactorIds), consumers.ToArray(), consumerInventory);
    }
    internal static string? RequireNotation(DimStyleTableRecord source, DimensionStyleDefaults364 changes, Transaction tr, Action cancel)
    {
        string symbols = changes.ToleranceMode == "deviation" ? "±" : "";
        if (changes.LinearUnitFormat is 3 or 4) symbols += "'\"";
        symbols += changes.AngularUnitFormat switch { 0 => "°", 1 => "°'\"", 4 => "NSEW°'\"", _ => "" };
        if (symbols.Length == 0) return null;
        cancel(); var textStyle = (TextStyleTableRecord)tr.GetObject(source.Dimtxsty, OpenMode.ForRead);
        const string notice = "DIMENSION_STYLE_NOTATION_UNVERIFIED: Generated engineering notation and retained SHX/big-font/inline runs need rendered review; complete glyph coverage is not claimed.";
        if (textStyle.IsShapeFile || !string.IsNullOrEmpty(textStyle.BigFontFileName) || string.IsNullOrWhiteSpace(textStyle.Font.TypeFace)) return notice;
        try { FontCoverage.Require(tr, source.Dimtxsty, symbols); }
        catch (DomainException error) when (error.Error.code == "FONT_CHECK_UNAVAILABLE") { return notice; }
        cancel(); return notice; // Selected literal symbols checked; full native expansion is still unobserved.
    }
    internal static void Verify(State before, EntityRef context, DimensionStyleChanges changes, DimensionStyleProperties expected,
        bool defaultsMayChange, Database db, Transaction tr, Action cancel)
    {
        var after = Capture(before.Style, context, db, tr, cancel, before.ConsumerInventory);
        Need(before.CurrentLayer == after.CurrentLayer && before.CurrentTextStyle == after.CurrentTextStyle &&
            before.CurrentDimensionStyle == after.CurrentDimensionStyle && Exact(before.StyleReactors, after.StyleReactors),
            "Dimension style default editing changed current record selection or native style dependencies.");
        var wanted = Wire.Element(expected); var paths = defaultsMayChange ? DimensionStyleDefaultsValidation364.Paths(changes) : [];
        bool effective = before.CurrentDimensionStyle == before.Style && defaultsMayChange;
        Need(DimensionStyleDefaultsValidation364.EffectiveFieldsRetained(before.CurrentDefaults, after.CurrentDefaults, wanted,
            effective ? paths : [], cancel, out var currentMismatch), "Unrequested current DIM defaults changed.", new { field = currentMismatch });
        Need(before.Consumers.Select(c => c.Id).SequenceEqual(after.Consumers.Select(c => c.Id)), "Native dimension style consumer membership changed.");
        for (int index = 0; index < before.Consumers.Length; index++)
        {
            cancel(); var old = before.Consumers[index]; var current = after.Consumers[index];
            Need(Exact(old.Stable, current.Stable), "Native default recompute changed a referring dimension's measured definition, explicit placement, identity, appearance or stored data.",
                new { dimension_handle = Id(old.Id), before_sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(old.Stable, Wire.Json))),
                    after_sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(current.Stable, Wire.Json))) });
            Need(DimensionStyleDefaultsValidation364.EffectiveFieldsRetained(old.Effective, current.Effective, wanted, paths, cancel, out var mismatch),
                "Native default recompute changed an unrequested effective field or produced neither the retained override nor the requested default.",
                new { dimension_handle = Id(old.Id), field = mismatch });
        }
    }
    internal static void Set(DimStyleTableRecord record, DimensionStyleDefaults364 changes, Action cancel)
    {
        // Call only explicitly requested setters; omitted variables and all
        // dependencies/colors/weights remain untouched and fully read back.
        if (changes.LinearPrecision is { } lp) { cancel(); record.Dimdec = lp; }
        if (changes.AngularPrecision is { } ap) { cancel(); record.Dimadec = ap; }
        if (changes.DecimalSeparator is { } ds) { cancel(); record.Dimdsep = ds[0]; }
        if (changes.LinearUnitFormat is { } lu) { cancel(); record.Dimlunit = lu; }
        if (changes.AngularUnitFormat is { } au) { cancel(); record.Dimaunit = au; }
        if (changes.Rounding is { } r) { cancel(); record.Dimrnd = r; }
        if (changes.LinearZeroSuppression is { } lz) { cancel(); record.Dimzin = lz; }
        if (changes.AngularZeroSuppression is { } az) { cancel(); record.Dimazin = az; }
        if (changes.ToleranceMode is { } tm) { cancel(); record.Dimtol = tm == "deviation"; cancel(); record.Dimlim = tm == "limits"; }
        if (changes.UpperDeviation is { } upper) { cancel(); record.Dimtp = upper; }
        if (changes.LowerDeviation is { } lower) { cancel(); record.Dimtm = -lower; }
        if (changes.TolerancePrecision is { } tp) { cancel(); record.Dimtdec = tp; }
        if (changes.ToleranceZeroSuppression is { } tz) { cancel(); record.Dimtzin = tz; }
        if (changes.ToleranceHeightFactor is { } tf) { cancel(); record.Dimtfac = tf; }
        if (changes.ExtensionOffset is { } eo) { cancel(); record.Dimexo = eo; }
        if (changes.ExtensionBeyond is { } eb) { cancel(); record.Dimexe = eb; }
        if (changes.TextGap is { } gap) { cancel(); record.Dimgap = gap; }
        cancel();
    }
}
