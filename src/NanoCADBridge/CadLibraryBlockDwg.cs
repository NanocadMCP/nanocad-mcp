using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Application = HostMgd.ApplicationServices.Application;
using Rx = Teigha.Runtime;

namespace NanoCADBridge;

// Library geometry is wblock-cloned from an independent read-only database.
// MangleName retains incoming symbol semantics without replacing target symbols.
// A redefinition changes only the non-ATTDEF members of the same native BTR.
internal static class CadLibraryBlockDwg
{
    const string Warning = "DWG_LIBRARY_BLOCK: Model-space roots and their supported static definition graph are natively cloned from a read-only Millimeters DWG. Conflicting incoming symbol names use native MangleName; existing target symbols are not replaced. Redefinition retains the target identity/base, ATTDEF identities/defaults and existing occurrence placements/attributes. Native owned metadata follows its cloning adapter; private payloads and evaluated graphics are not exhaustively observed. Refresh inspect_block and inspect the useful shared-symbol view. Undo/rollback is not guaranteed; unknown never means success or rollback.";
    const string Mismatch = "NATIVE_BLOCK_LIBRARY_MISMATCH";
    sealed record SourceEntity(ObjectId Id, ObjectId Owner, ObjectId[] Attributes, ObjectId? Definition, JsonElement Original, JsonElement Expected, JsonElement Metadata);
    sealed record SourceDefinition(ObjectId Id, ObjectId[] Members, JsonElement Value, JsonElement Metadata);
    sealed record SourceSymbol(ObjectId Id, string Name, JsonElement Value, JsonElement Metadata);
    sealed record Storage(ObjectId Id, ObjectId Owner, string Role, JsonElement Common, JsonElement Metadata);
    sealed record SymbolWitness(ObjectId Id, ObjectId Owner, string Name, string RuntimeType, JsonElement Metadata);
    sealed record NativeClass(bool Exact, object Observation);
    sealed record Target(CadBlockDefinitionGeometry388.State? Before, ObjectId[] Removed, Dictionary<ObjectId, ObjectId[]> RetiredAttributes,
        ObjectId[] AttributeDefinitions, ObjectId[] RetainedDefinitions, Point3d Origin, string Name);
    static void Need([DoesNotReturnIf(false)] bool condition, string message, string code = "UNSUPPORTED_BLOCK_LIBRARY")
    { if (!condition) throw new DomainException(code, message); }
    static bool Live(ObjectId id) => !id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased;
    static string Id(ObjectId id) => Validation.NormalizeHandle(id.Handle.ToString());
    static string? MaybeId(ObjectId id) => id.IsNull ? null : Id(id);
    static string NativeHandle(string text)
    {
        Need(text.Length is > 0 and <= 16 && ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _),
            "A typed native metadata handle is not a valid hexadecimal DWG handle.");
        return ulong.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString("X", CultureInfo.InvariantCulture);
    }
    static double[] Point(Point3d p) => [p.X, p.Y, p.Z];
    static void Plane(Point3d p)
    { Need(double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z) && Math.Abs(p.Z) <= 1e-9, "Library bases and nested definition origins must be finite WCS XY points at Z=0.", "INVALID_GEOMETRY"); }
    internal static bool Handles(StructureWriteArguments args) => args.Operations is [LibraryBlockDwgEdit];
    static void Same(JsonElement expected, JsonElement actual, string message)
    { Need(BlockMoveGeometry.SameProperties(expected, actual), message, Mismatch); }
    static void SameMetadata(JsonElement expected, JsonElement actual, DBObject source, DBObject target, string role, string message,
        string scope = "Fully mapped source metadata versus actual destination metadata; one differing JSON pointer is reported.", object? nativeCreationTime = null)
    {
        if (NativeMetadataEquality.Same(expected, actual)) return;
        throw new DomainException(Mismatch, message, false, new
        {
            classification = role,
            source_handle = Id(source.ObjectId),
            source_owner = MaybeId(source.OwnerId),
            source_type = source.GetType().FullName,
            target_handle = Id(target.ObjectId),
            target_owner = MaybeId(target.OwnerId),
            target_type = target.GetType().FullName,
            comparison_scope = scope,
            first_difference = LibraryBlockMetadataComparison.FirstDifference(expected, actual),
            native_creation_time = nativeCreationTime
        });
    }
    static void Exact(JsonElement expected, JsonElement actual, string message)
    { Need(expected.GetRawText() == actual.GetRawText(), message, Mismatch); }
    static void Independent(DBObject value)
    { using var links = value.GetPersistentReactorIds(); Need(!value.HasFields && links.Count == 0, "Library sources and the target definition must be independent of Fields and persistent associations."); }

    static JsonElement Metadata(DBObject value, Transaction tr, Action cancel)
    {
        Independent(value); var owned = NativeOwnedMetadata.Capture(value, tr, cancel);
        foreach (var node in owned.EnumerateArray())
        { cancel(); Need(node.GetProperty("common").GetProperty("reactors").GetArrayLength() == 0, "Owned source metadata has persistent associations requiring its owning route."); }
        using var xdata = value.XData; var budget = new ModelInventoryBudget(cancel);
        return Wire.Element(new { extension = MaybeId(value.ExtensionDictionary), xdata = xdata == null ? null : NativeOwnedMetadata.Buffer(xdata, cancel, budget.Add), owned });
    }
    static bool HasMetadataPayload(JsonElement value) => value.GetProperty("extension").ValueKind != JsonValueKind.Null ||
        value.GetProperty("xdata").ValueKind != JsonValueKind.Null || value.GetProperty("owned").GetArrayLength() != 0;
    static object? SymbolEvidence(DBObject value, Transaction? tr, Action cancel)
    {
        if (value is not SymbolTableRecord symbol) return null;
        if (tr == null) return new
        {
            name = (string?)null,
            metadata = (LibraryBlockObservedValue?)null,
            metadata_scope = "native_metadata_unavailable_no_live_transaction",
            metadata_error = "The target observation transaction has ended."
        };
        string? name = null;
        try
        {
            name = symbol.Name;
            var metadata = Metadata(symbol, tr, cancel);
            var observed = LibraryBlockMetadataComparison.FirstDifference(JsonSerializer.SerializeToElement<object?>(null, Wire.Json), metadata)!.actual;
            return new { name, metadata = observed, metadata_scope = "actual_native_owned_metadata_snapshot", metadata_error = (string?)null };
        }
        catch (OperationCanceledException) { throw; }
        catch (System.Exception error)
        {
            return new
            {
                name,
                metadata = (LibraryBlockObservedValue?)null,
                metadata_scope = "native_metadata_unavailable_no_empty_payload_inferred",
                metadata_error = error is DomainException domain ? domain.Error.code : error.GetType().FullName
            };
        }
    }
    static Type? StorageType(string role) => role switch { "block_begin" => typeof(BlockBegin), "block_end" => typeof(BlockEnd), "sequence_end" => typeof(SequenceEnd), _ => null };
    static NativeClass ClassIdentity(DBObject value, Type? expectedType = null)
    {
        Rx.RXClass? actual = null, expected = null;
        try
        {
            actual = value.GetRXClass(); if (expectedType != null) expected = Rx.RXObject.GetClass(expectedType);
            if (actual?.AutoDelete == true || expected?.AutoDelete == true)
                throw new DomainException(Mismatch, "A native class descriptor is not a proved borrowed wrapper.");
            bool actualLive = actual != null && actual.UnmanagedObject != IntPtr.Zero, expectedLive = expected != null && expected.UnmanagedObject != IntPtr.Zero;
            bool exact = actualLive && expectedLive && actual!.UnmanagedObject == expected!.UnmanagedObject;
            return new(exact, new
            {
                expected_managed_type = expectedType?.FullName,
                actual_descriptor_available = actualLive,
                expected_descriptor_available = expectedLive,
                exact_descriptor = exact,
                actual_name = actualLive ? actual!.Name : null,
                actual_dxf_name = actualLive ? actual!.DxfName : null,
                expected_name = expectedLive ? expected!.Name : null,
                expected_dxf_name = expectedLive ? expected!.DxfName : null,
                scope = "Exact native RX descriptor equality; CLR wrappers and class names do not establish subtype."
            });
        }
        finally
        {
            foreach (var descriptor in new[] { actual, expected }.OfType<Rx.RXClass>().Distinct(ReferenceEqualityComparer.Instance))
                if (descriptor is Rx.RXClass item) { if (!item.AutoDelete) item.Dispose(); else GC.SuppressFinalize(item); }
            GC.KeepAlive(value);
        }
    }
    static JsonElement DefinitionValue(BlockTableRecord b) => Wire.Element(new { origin = Point(b.Origin), units = (int)b.Units, scaling = (int)b.BlockScaling, b.Explodable });
    static Storage StorageState(Entity marker, ObjectId owner, string role, Database db, Transaction tr, Action cancel)
    {
        Need(Live(marker.ObjectId) && marker.OwnerId == owner && marker.Database?.UnmanagedObject == db.UnmanagedObject, "A native storage marker has unavailable or foreign ownership.");
        var native = ClassIdentity(marker, StorageType(role));
        if (!native.Exact) throw new DomainException(Mismatch, "A native storage marker does not have the exact expected native role class.", false, native.Observation);
        var common = Wire.Element(new { layer = MaybeId(marker.LayerId), linetype = MaybeId(marker.LinetypeId), display = CadBlockMover.PreservedProperties(marker), annotative = marker.Annotative.ToString() });
        return new(marker.ObjectId, owner, role, common, Metadata(marker, tr, cancel));
    }
    static Storage[] Bookends(BlockTableRecord block, Database db, Transaction tr, Action cancel)
    {
        var result = new List<Storage>();
        foreach (var item in new (ObjectId Id, Type Type, string Role)[] { (block.BlockBeginId, typeof(BlockBegin), "block_begin"), (block.BlockEndId, typeof(BlockEnd), "block_end") })
        {
            cancel(); Need(Live(item.Id) && tr.GetObject(item.Id, OpenMode.ForRead) is Entity, "A native block storage marker is unavailable.");
            var marker = (Entity)tr.GetObject(item.Id, OpenMode.ForRead); var native = ClassIdentity(marker, item.Type);
            if (!native.Exact) throw new DomainException(Mismatch, "A native block storage getter returned a different native marker class.", false, native.Observation);
            result.Add(StorageState(marker, block.ObjectId, item.Role, db, tr, cancel));
        }
        return result.ToArray();
    }
    static bool LayoutContext(DBObject value, Database db, Transaction tr, out string? kind)
    {
        kind = null; if (value.GetType() != typeof(BlockTableRecord) || value is not BlockTableRecord block || !block.IsLayout) return false;
        try { kind = CadBlockSpace.RequireSpace(db, tr, block.ObjectId).Kind; return true; }
        catch (DomainException) { return false; }
    }
    static Storage[] LayoutBookendWitnesses(Database db, Transaction tr, Action cancel)
    {
        var result = new List<Storage>(); var budget = new ModelInventoryBudget(cancel); var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in layouts)
        {
            cancel(); Need(Live(entry.Value) && tr.GetObject(entry.Value, OpenMode.ForRead) is Layout, "A native target layout context is unavailable.");
            var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead); _ = CadBlockSpace.RequireSpace(db, tr, layout.BlockTableRecordId);
            var block = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            foreach (var marker in Bookends(block, db, tr, cancel)) { budget.Add(new { marker.Common, marker.Metadata }); result.Add(marker); }
        }
        return result.ToArray();
    }

    static CadBlockMover.Preview Detached(BlockReference original, Transaction tr, Action cancel)
    {
        BlockReference root = original is MInsertBlock array
            ? new MInsertBlock(original.Position, original.BlockTableRecord, array.Columns, array.Rows, array.ColumnSpacing, array.RowSpacing)
            : new BlockReference(original.Position, original.BlockTableRecord);
        var attributes = new List<AttributeReference>();
        try
        {
            root.Normal = original.Normal; root.Rotation = original.Rotation; root.ScaleFactors = original.ScaleFactors; root.SetPropertiesFrom(original);
            foreach (ObjectId id in original.AttributeCollection)
            {
                cancel(); var source = (AttributeReference)tr.GetObject(id, OpenMode.ForRead);
                var copy = source.IsMTextAttribute ? (AttributeReference)NativeBlockMultiline442.Detached(source, cancel) : (AttributeReference)source.Clone();
                attributes.Add(copy); Need(copy.ObjectId.IsNull, "A source attribute oracle must be detached.");
            }
            return new(root, attributes.ToArray());
        }
        catch { foreach (var attribute in attributes) attribute.Dispose(); root.Dispose(); throw; }
    }
    static JsonElement Snapshot(Entity entity, Transaction tr, Action cancel, Vector3d offset)
    {
        object Geometry(Entity value) => value is AttributeReference attribute ? CadBlockMover.AttributeSnapshot(attribute, cancel) : CadBlockDefinitionGeometry388.StoredGeometry(value, cancel);
        object Value(Entity value, object geometry) => new { geometry, layer = MaybeId(value.LayerId), linetype = MaybeId(value.LinetypeId), display = CadBlockMover.PreservedProperties(value) };
        if (entity is BlockReference reference)
        {
            using var preview = Detached(reference, tr, cancel);
            if (offset.Length > 0)
            {
                var matrix = Matrix3d.Displacement(offset); preview.Reference.TransformBy(matrix);
                foreach (var attribute in preview.Attributes) { cancel(); attribute.TransformBy(matrix); }
            }
            return Wire.Element(Value(preview.Reference, CadBlockMover.Snapshot(preview, cancel)));
        }
        if (offset.Length == 0) return Wire.Element(Value(entity, Geometry(entity)));
        using var clone = entity is AttributeReference multiline && multiline.IsMTextAttribute
            ? (Entity)NativeBlockMultiline442.Detached(multiline, cancel) : (Entity)entity.Clone();
        Need(clone.ObjectId.IsNull, "A source geometry oracle must be detached."); clone.TransformBy(Matrix3d.Displacement(offset));
        return Wire.Element(Value(clone, Geometry(clone)));
    }
    static object Color(Teigha.Colors.Color c) => new { method = c.ColorMethod.ToString(), aci = c.IsByAci ? (int?)c.ColorIndex : null, rgb = c.IsByColor ? new[] { (int)c.Red, c.Green, c.Blue } : null };
    // These public property/method names were checked in installed hostdbmgd
    // metadata. IDs, including shape-text styles, are translated by IdMapping.
    static JsonElement SymbolValue(SymbolTableRecord symbol, Action cancel)
    {
        if (symbol is LayerTableRecord layer)
        {
            using var color = layer.Color; var t = layer.Transparency;
            return Wire.Element(new
            {
                type = "layer",
                color = Color(color),
                layer.IsFrozen,
                layer.IsHidden,
                layer.IsLocked,
                layer.IsOff,
                layer.IsPlottable,
                layer.Description,
                lineweight = (int)layer.LineWeight,
                layer.ViewportVisibilityDefault,
                linetype = MaybeId(layer.LinetypeObjectId),
                material = MaybeId(layer.MaterialId),
                transparency = new { t.Alpha, t.IsByAlpha, t.IsByLayer, t.IsByBlock }
            });
        }
        if (symbol is TextStyleTableRecord style)
        {
            var font = style.Font;
            return Wire.Element(new
            {
                type = "text_style",
                style.BigFontFileName,
                style.FileName,
                style.FlagBits,
                style.IsShapeFile,
                style.IsVertical,
                style.ObliquingAngle,
                style.PriorSize,
                style.TextSize,
                style.XScale,
                font.TypeFace,
                font.Bold,
                font.Italic,
                font.CharacterSet,
                font.PitchAndFamily
            });
        }
        if (symbol is LinetypeTableRecord linetype)
        {
            var rows = new List<object>(); var budget = new ModelInventoryBudget(cancel);
            for (int i = 0; i < linetype.NumDashes; i++)
            {
                cancel(); var p = linetype.ShapeOffsetAt(i);
                var row = new
                {
                    length = linetype.DashLengthAt(i),
                    ucs = linetype.ShapeIsUcsOrientedAt(i),
                    shape = linetype.ShapeNumberAt(i),
                    offset = new[] { p.X, p.Y },
                    rotation = linetype.ShapeRotationAt(i),
                    scale = linetype.ShapeScaleAt(i),
                    style = MaybeId(linetype.ShapeStyleAt(i)),
                    text = linetype.TextAt(i)
                };
                budget.Add(row); rows.Add(row);
            }
            return Wire.Element(new { type = "linetype", linetype.AsciiDescription, linetype.Comments, linetype.IsScaledToFit, linetype.PatternLength, dashes = rows.ToArray() });
        }
        throw new DomainException("UNSUPPORTED_BLOCK_LIBRARY", "A referenced library symbol has no supported semantic reader.");
    }
    static Dictionary<ObjectId, SymbolWitness> ExistingSymbolWitnesses(Source source, Database db, Transaction tr, Action cancel)
    {
        var result = new Dictionary<ObjectId, SymbolWitness>(); var budget = new ModelInventoryBudget(cancel);
        foreach (var item in source.Symbols.Values)
        {
            cancel(); var original = (SymbolTableRecord)source.Transaction.GetObject(item.Id, OpenMode.ForRead); ObjectId existing;
            if (original.GetType() == typeof(LayerTableRecord))
            { var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead); if (!table.Has(item.Name)) continue; existing = table[item.Name]; }
            else if (original.GetType() == typeof(LinetypeTableRecord))
            { var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead); if (!table.Has(item.Name)) continue; existing = table[item.Name]; }
            else if (original.GetType() == typeof(TextStyleTableRecord))
            { var table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead); if (!table.Has(item.Name)) continue; existing = table[item.Name]; }
            else throw new DomainException("UNSUPPORTED_BLOCK_LIBRARY", "A source symbol has no admitted native target table.");
            if (result.ContainsKey(existing)) continue;
            Need(Live(existing) && tr.GetObject(existing, OpenMode.ForRead) is SymbolTableRecord, "A potentially reused native target symbol is unavailable.");
            var actual = (SymbolTableRecord)tr.GetObject(existing, OpenMode.ForRead);
            Need(actual.GetType() == original.GetType() && actual.Database?.UnmanagedObject == db.UnmanagedObject, "A potentially reused target symbol has a different native type/database.");
            var metadata = Metadata(actual, tr, cancel); budget.Add(new { handle = Id(existing), owner = Id(actual.OwnerId), actual.Name, metadata });
            result.Add(existing, new(existing, actual.OwnerId, actual.Name, actual.GetType().FullName!, metadata));
        }
        return result;
    }
    static bool OrdinarySymbolIndex(SymbolTableRecord record, Database db, Transaction tr)
    {
        if (!Live(record.ObjectId) || record.Database?.UnmanagedObject != db.UnmanagedObject || record.IsDependent) return false;
        if (record.GetType() == typeof(LayerTableRecord) && record.OwnerId == db.LayerTableId)
        { var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead); return table.Has(record.Name) && table[record.Name] == record.ObjectId; }
        if (record.GetType() == typeof(LinetypeTableRecord) && record.OwnerId == db.LinetypeTableId)
        { var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead); return table.Has(record.Name) && table[record.Name] == record.ObjectId; }
        if (record.GetType() == typeof(TextStyleTableRecord) && record.OwnerId == db.TextStyleTableId)
        { var table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead); return table.Has(record.Name) && table[record.Name] == record.ObjectId; }
        if (record.GetType() == typeof(RegAppTableRecord) && record.OwnerId == db.RegAppTableId && ClassIdentity(record, typeof(RegAppTableRecord)).Exact)
        {
            var table = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead); return table.GetType() == typeof(RegAppTable) && table.ObjectId == record.OwnerId &&
                table.Database?.UnmanagedObject == db.UnmanagedObject && table.Has(record.Name) && table[record.Name] == record.ObjectId;
        }
        return false;
    }

    sealed class Source : IDisposable
    {
        readonly FileStream stream;
        internal readonly Database Database;
        internal readonly Transaction Transaction;
        internal readonly string Path, Sha256;
        internal readonly Point3d Base;
        internal readonly ObjectId[] Roots;
        internal readonly ObjectId ModelSpace;
        internal readonly Dictionary<ObjectId, SourceEntity> Entities = [];
        internal readonly Dictionary<ObjectId, SourceDefinition> Definitions = [];
        internal readonly Dictionary<ObjectId, SourceSymbol> Symbols = [];
        internal readonly Dictionary<ObjectId, string> ReceiptSymbols = [];
        internal readonly Dictionary<ObjectId, Storage> Storages = [];
        readonly HashSet<ObjectId> ownedMetadata = [];
        internal bool ConsumesMapping(ObjectId id) => id == ModelSpace || Entities.ContainsKey(id) || Definitions.ContainsKey(id) ||
            Symbols.ContainsKey(id) || ReceiptSymbols.ContainsKey(id) || Storages.ContainsKey(id) || ownedMetadata.Contains(id);
        readonly ModelInventoryBudget inventoryBudget;
        internal readonly HashSet<string> Warnings = new(StringComparer.Ordinal);
        readonly Vector3d offset;
        readonly Action cancel;
        readonly Action<string>? checkpoint;
        internal Source(LibraryBlockDwgEdit edit, Point3d? targetOrigin, Action check, Action<string>? trace = null)
        {
            cancel = check; inventoryBudget = new(check); checkpoint = trace; Path = edit.SourcePath; Database = null!; Transaction = null!; stream = null!; Roots = [];
            try
            {
                // Holding this handle denies writes/deletion while native code
                // opens its own read-only handle. Neither database is saved.
                stream = new(Path, FileMode.Open, FileAccess.Read, FileShare.Read); Sha256 = Hash();
                Need(edit.ExpectedFileSha256 == null || edit.ExpectedFileSha256 == Sha256, "The DWG file hash changed; refresh the selected library file.", "STALE_LIBRARY_FILE");
                checkpoint?.Invoke("source_database_construct_before"); Database = new(false, true); checkpoint?.Invoke("source_database_construct_after");
                checkpoint?.Invoke("source_dwg_read_before"); Database.ReadDwgFile(Path, FileOpenMode.OpenForReadAndAllShare, false, null); checkpoint?.Invoke("source_dwg_read_after");
                checkpoint?.Invoke("source_input_close_before"); Database.CloseInput(true); checkpoint?.Invoke("source_input_close_after");
                Need((int)Database.Insunits == 4, "The library DWG must explicitly use Millimeters; no unit conversion is performed.", "UNSUPPORTED_UNITS");
                Base = Database.Insbase; Plane(Base); offset = (targetOrigin ?? Base) - Base;
                if (edit.Action == "import_block_dwg") _ = new BlockPlacement(new(Base.X, Base.Y), new(0, 0), 0, 1).Matrix();
                checkpoint?.Invoke("source_graph_before"); Transaction = Database.TransactionManager.StartTransaction(); var blocks = (BlockTable)Transaction.GetObject(Database.BlockTableId, OpenMode.ForRead);
                var model = (BlockTableRecord)Transaction.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                ModelSpace = model.ObjectId;
                Roots = CadBlockDefinitionGeometry388.Members(model, Database, Transaction, cancel);
                Need(Roots.Length > 0, "The library DWG model space contains no live supported roots.", "EMPTY_BLOCK_LIBRARY");
                var budget = inventoryBudget; var rootTags = new HashSet<string>(StringComparer.Ordinal); int primitiveCount = 0; bool closedSpline = false;
                foreach (var marker in Bookends(model, Database, Transaction, cancel))
                {
                    // Requested roots use the explicit destination BTR owner.
                    // Native database-context mappings remain separate; the
                    // file's ModelSpace is not copied as a second definition.
                    Need(marker.Metadata.GetProperty("extension").ValueKind == JsonValueKind.Null && marker.Metadata.GetProperty("xdata").ValueKind == JsonValueKind.Null,
                        "Model-space container markers carry user metadata outside the requested root-entity import scope; no such payload is omitted.");
                    AddStorage(marker);
                }
                foreach (var root in Roots)
                {
                    cancel(); var entity = (Entity)Transaction.GetObject(root, OpenMode.ForRead);
                    if (entity is AttributeDefinition attribute)
                    {
                        Need(edit.Action == "import_block_dwg", "Redefinition rejects source root ATTDEFs because the target ATTDEF identities/defaults must remain unchanged.");
                        Need(BlockWriteValidation.Tag(attribute.Tag) && rootTags.Add(attribute.Tag), "Imported root ATTDEFs require supported unique attribute tags.", "UNSUPPORTED_BLOCK_CONTENT");
                        var layers = (LayerTable)Transaction.GetObject(Database.LayerTableId, OpenMode.ForRead);
                        Need(!attribute.Constant && !attribute.Invisible && !attribute.IsMTextAttributeDefinition && attribute.HorizontalMode == TextHorizontalMode.TextLeft &&
                            attribute.VerticalMode == TextVerticalMode.TextBase && attribute.WidthFactor == 1 && attribute.Oblique == 0 && attribute.LayerId == layers["0"] &&
                            StructureWriteValidation.IsPlainAttribute(attribute.TextString), "Imported root attributes require visible variable left/baseline plain single-line layer0 ATTDEFs supported by native insertion.");
                        Need(Transaction.GetObject(attribute.TextStyleId, OpenMode.ForRead) is TextStyleTableRecord style && style.TextSize == 0,
                            "Imported root ATTDEFs require a native insertion-supported text style without fixed height.");
                    }
                    else
                    {
                        Need(entity is not AttributeReference, "A model-space AttributeReference is not an insertable block primitive.", "UNSUPPORTED_BLOCK_CONTENT");
                        primitiveCount++; closedSpline |= entity is Spline { Closed: true };
                        if (entity.GetType() == typeof(DBText)) Need(StructureWriteValidation.IsPlainAttribute(((DBText)entity).TextString),
                            "Imported root Text must be bounded plain text without Fields or formatting codes supported by native block insertion.", "UNSUPPORTED_BLOCK_CONTENT");
                    }
                    WalkEntity(root, model.ObjectId, offset, new HashSet<ObjectId>(), budget);
                }
                Need(primitiveCount > 0, "An insertable library definition requires at least one supported primitive in addition to any root ATTDEFs.", "UNSUPPORTED_BLOCK_CONTENT");
                Need(!closedSpline || rootTags.Count == 0, "Closed spline block members require an attribute-free definition under the native insertion guard.", "UNSUPPORTED_BLOCK_CONTENT");
                // Native cloning may also register/rename XData applications.
                // Their actual native IDs/names count in the receipt budget.
                foreach (var entity in Entities.Values) RememberOwned(entity.Metadata);
                foreach (var definition in Definitions.Values) RememberOwned(definition.Metadata);
                foreach (var symbol in Symbols.Values) RememberOwned(symbol.Metadata);
                foreach (var storage in Storages.Values) RememberOwned(storage.Metadata);
                foreach (var entity in Entities.Values) { ReceiptReferences(entity.Metadata); ReceiptReferences(entity.Original); }
                foreach (var definition in Definitions.Values) ReceiptReferences(definition.Metadata);
                foreach (var symbol in Symbols.Values) ReceiptReferences(symbol.Metadata);
                foreach (var storage in Storages.Values) ReceiptReferences(storage.Metadata);
                VerifyFile(); checkpoint?.Invoke("source_graph_after");
            }
            catch { Dispose(); throw; }
        }
        string Hash() { cancel(); stream.Position = 0; using var algorithm = SHA256.Create(); string hash = Convert.ToHexString(algorithm.ComputeHash(stream)); stream.Position = 0; cancel(); return hash; }
        internal void VerifyFile() => Need(Hash() == Sha256, "The read-only library DWG changed during the operation.", "STALE_LIBRARY_FILE");
        internal void AddStorage(Storage storage)
        {
            Need(Storages.TryAdd(storage.Id, storage), "A native storage marker has repeated identity.");
            var marker = (Entity)Transaction.GetObject(storage.Id, OpenMode.ForRead);
            Symbol(marker.LayerId, inventoryBudget); Symbol(marker.LinetypeId, inventoryBudget);
            inventoryBudget.Add(new { storage.Common, storage.Metadata });
        }
        internal void ValidateStorageReferences()
        {
            foreach (var storage in Storages.Values) RememberOwned(storage.Metadata);
            foreach (var symbol in Symbols.Values) RememberOwned(symbol.Metadata);
            foreach (var storage in Storages.Values) ReceiptReferences(storage.Metadata);
            foreach (var symbol in Symbols.Values) ReceiptReferences(symbol.Metadata);
        }
        void RememberOwned(JsonElement metadata)
        {
            foreach (var record in metadata.GetProperty("owned").EnumerateArray())
            { cancel(); ownedMetadata.Add(CadStructureWriter.Find(Database, record.GetProperty("common").GetProperty("handle").GetString()!)); }
        }
        void Reference(string suppliedHandle, bool nullAllowed = false)
        {
            string handle = NativeHandle(suppliedHandle); if (nullAllowed && handle == "0") return;
            var id = CadStructureWriter.Find(Database, handle); Need(Live(id), "A typed source metadata reference is unavailable.");
            var referenced = Transaction.GetObject(id, OpenMode.ForRead);
            Need(referenced.Database?.UnmanagedObject == Database.UnmanagedObject && referenced.ObjectId == id, "A typed source metadata reference has foreign native identity.");
            if (id == ModelSpace || Entities.ContainsKey(id) || Definitions.ContainsKey(id) || Symbols.ContainsKey(id) || Storages.ContainsKey(id) || ownedMetadata.Contains(id)) return;
            if (referenced.GetType() == typeof(RegAppTableRecord) && referenced.OwnerId == Database.RegAppTableId)
            { ReceiptSymbols.TryAdd(id, ((RegAppTableRecord)referenced).Name); return; }
            Need(false, "Typed source metadata references an object outside the supported library geometry, symbol and owned-data closure; its owning route is required.");
        }
        void ReceiptReferences(JsonElement value)
        {
            cancel();
            if (value.ValueKind == JsonValueKind.Array) { foreach (var item in value.EnumerateArray()) ReceiptReferences(item); return; }
            if (value.ValueKind != JsonValueKind.Object) return;
            if (value.TryGetProperty("code", out var code) && code.TryGetInt32(out int group) && group == 1001 && value.TryGetProperty("value", out var data) && data.ValueKind == JsonValueKind.String)
            {
                var applications = (RegAppTable)Transaction.GetObject(Database.RegAppTableId, OpenMode.ForRead); string name = data.GetString()!;
                Need(applications.Has(name), "A source XData application registration is unavailable."); var id = applications[name]; ReceiptSymbols.TryAdd(id, ((RegAppTableRecord)Transaction.GetObject(id, OpenMode.ForRead)).Name);
            }
            if (value.TryGetProperty("native_type", out var native) && native.ValueKind == JsonValueKind.String && native.GetString() == "ObjectId" && value.TryGetProperty("handle", out var handle) && handle.ValueKind == JsonValueKind.String)
                Reference(handle.GetString()!);
            if (LibraryBlockMetadataReferences.HandleReference(value) is { } typedHandle) Reference(typedHandle, nullAllowed: true);
            if (value.TryGetProperty("owned", out var owns) && owns.ValueKind is JsonValueKind.False or JsonValueKind.True &&
                value.TryGetProperty("owner_handle", out _) && value.TryGetProperty("handle", out var entry) && entry.ValueKind == JsonValueKind.String)
                Reference(entry.GetString()!);
            foreach (var property in value.EnumerateObject()) ReceiptReferences(property.Value);
        }
        void Symbol(ObjectId id, ModelInventoryBudget budget)
        {
            if (id.IsNull || Symbols.ContainsKey(id)) return; cancel();
            Need(Live(id) && Transaction.GetObject(id, OpenMode.ForRead) is SymbolTableRecord, "A library symbol is unavailable.");
            var symbol = (SymbolTableRecord)Transaction.GetObject(id, OpenMode.ForRead);
            Need(!symbol.IsDependent && symbol.Database?.UnmanagedObject == Database.UnmanagedObject, "A library symbol is foreign/dependent."); Independent(symbol);
            Need(symbol.GetType() == typeof(LayerTableRecord) && symbol.OwnerId == Database.LayerTableId ||
                symbol.GetType() == typeof(LinetypeTableRecord) && symbol.OwnerId == Database.LinetypeTableId ||
                symbol.GetType() == typeof(TextStyleTableRecord) && symbol.OwnerId == Database.TextStyleTableId,
                "A library symbol has a custom runtime type or foreign table owner.");
            var snapshot = SymbolValue(symbol, cancel); var metadata = Metadata(symbol, Transaction, cancel); var item = new SourceSymbol(id, symbol.Name, snapshot, metadata);
            Symbols.Add(id, item); ReceiptSymbols.TryAdd(id, symbol.Name); budget.Add(new { snapshot, metadata });
            if (symbol is LayerTableRecord layer) Symbol(layer.LinetypeObjectId, budget);
            if (symbol is LinetypeTableRecord line) for (int i = 0; i < line.NumDashes; i++) { cancel(); Symbol(line.ShapeStyleAt(i), budget); }
        }
        void WalkEntity(ObjectId id, ObjectId owner, Vector3d translation, HashSet<ObjectId> path, ModelInventoryBudget budget)
        {
            cancel(); if (Entities.ContainsKey(id)) return;
            Need(Live(id) && Transaction.GetObject(id, OpenMode.ForRead) is Entity, "A library member is unavailable."); var entity = (Entity)Transaction.GetObject(id, OpenMode.ForRead);
            Need(entity.ObjectId == id && entity.OwnerId == owner && entity.Database?.UnmanagedObject == Database.UnmanagedObject, "A library entity has foreign ownership."); Independent(entity);
            ObjectId[] attributes = []; ObjectId? child = null;
            if (entity is BlockReference reference)
            {
                CadBlockMover.CheckRoot(reference); _ = CadBlockPlacementGraph.Resolve(reference, Database, Transaction, cancel);
                attributes = reference.AttributeCollection.Cast<ObjectId>().ToArray(); child = reference.BlockTableRecord;
            }
            else CadBlockPlacementGraph.CheckLeaf(entity, Database, Transaction, cancel);
            if (entity is DBText or MText) FontCoverage.RequireEntity(Transaction, entity, warning => Warnings.Add(warning), cancel);
            Symbol(entity.LayerId, budget); Symbol(entity.LinetypeId, budget);
            if (entity is DBText text) Symbol(text.TextStyleId, budget); else if (entity is MText mtext) Symbol(mtext.TextStyleId, budget);
            var original = Snapshot(entity, Transaction, cancel, new Vector3d(0, 0, 0)); var expected = translation.Length == 0 ? original : Snapshot(entity, Transaction, cancel, translation);
            var metadata = Metadata(entity, Transaction, cancel); var row = new SourceEntity(id, owner, attributes, child, original, expected, metadata); Entities.Add(id, row); budget.Add(new { original, expected, metadata });
            foreach (var attribute in attributes) WalkEntity(attribute, id, translation, path, budget);
            if (child is not { } definition) return;
            Need(path.Add(definition), "The library contains cyclic static definitions.", "CYCLIC_BLOCK_DEFINITION");
            if (!Definitions.ContainsKey(definition))
            {
                var block = CadBlockDefinitionGeometry388.Definition(definition, Database, Transaction, false); Plane(block.Origin); Independent(block);
                var members = CadBlockDefinitionGeometry388.Members(block, Database, Transaction, cancel); var value = DefinitionValue(block); var owned = Metadata(block, Transaction, cancel);
                Definitions.Add(definition, new(definition, members, value, owned)); ReceiptSymbols.TryAdd(definition, block.Name); budget.Add(new { value, owned });
                foreach (var marker in Bookends(block, Database, Transaction, cancel)) AddStorage(marker);
                foreach (var member in members) WalkEntity(member, definition, new Vector3d(0, 0, 0), path, budget);
            }
            path.Remove(definition);
        }
        internal void Verify()
        {
            foreach (var row in Entities.Values)
            {
                cancel(); var entity = (Entity)Transaction.GetObject(row.Id, OpenMode.ForRead);
                Need(Live(row.Id) && entity.OwnerId == row.Owner, "Native cloning changed a retained source identity/owner.", Mismatch);
                Exact(row.Original, Snapshot(entity, Transaction, cancel, new Vector3d(0, 0, 0)), "Native cloning changed retained source geometry/display.");
                SameMetadata(row.Metadata, Metadata(entity, Transaction, cancel), entity, entity, "retained_source_entity_metadata",
                    "Native cloning changed retained source metadata.", "Retained source metadata snapshot versus the current source object.");
            }
            foreach (var row in Definitions.Values)
            {
                var definition = (BlockTableRecord)Transaction.GetObject(row.Id, OpenMode.ForRead); cancel();
                Need(CadBlockDefinitionGeometry388.Members(definition, Database, Transaction, cancel).SequenceEqual(row.Members), "Native cloning changed a source definition membership.", Mismatch);
                Exact(row.Value, DefinitionValue(definition), "Native cloning changed a source definition base/properties.");
                SameMetadata(row.Metadata, Metadata(definition, Transaction, cancel), definition, definition, "retained_source_definition_metadata",
                    "Native cloning changed source definition metadata.", "Retained source metadata snapshot versus the current source object.");
            }
            foreach (var row in Symbols.Values)
            {
                cancel(); var symbol = (SymbolTableRecord)Transaction.GetObject(row.Id, OpenMode.ForRead); Need(symbol.Name == row.Name, "Native cloning renamed a retained source symbol.", Mismatch);
                Exact(row.Value, SymbolValue(symbol, cancel), "Native cloning changed a retained source symbol.");
                SameMetadata(row.Metadata, Metadata(symbol, Transaction, cancel), symbol, symbol, "retained_source_symbol_metadata",
                    "Native cloning changed retained source symbol metadata.", "Retained source metadata snapshot versus the current source object.");
            }
            foreach (var row in Storages.Values)
            {
                cancel(); var marker = (Entity)Transaction.GetObject(row.Id, OpenMode.ForRead); var actual = StorageState(marker, row.Owner, row.Role, Database, Transaction, cancel);
                Exact(row.Common, actual.Common, "Native cloning changed retained source marker data.");
                SameMetadata(row.Metadata, actual.Metadata, marker, marker, "retained_source_storage_metadata",
                    "Native cloning changed retained source marker metadata.", "Retained source metadata snapshot versus the current source object.");
                if (row.Role is "block_begin" or "block_end")
                {
                    var owner = (BlockTableRecord)Transaction.GetObject(row.Owner, OpenMode.ForRead);
                    Need((row.Role == "block_begin" ? owner.BlockBeginId : owner.BlockEndId) == row.Id, "A retained source marker no longer matches its actual block getter.", Mismatch);
                }
            }
            VerifyFile();
        }
        public void Dispose()
        {
            try { checkpoint?.Invoke("source_transaction_dispose_before"); Transaction?.Dispose(); checkpoint?.Invoke("source_transaction_dispose_after"); }
            finally
            {
                try { checkpoint?.Invoke("source_database_dispose_before"); Database?.Dispose(); checkpoint?.Invoke("source_database_dispose_after"); }
                finally { stream?.Dispose(); }
            }
        }
    }

    static Target ResolveTarget(StructureWriteArguments args, DocumentState state, Transaction tr, Action cancel)
    {
        Need(Handles(args), "Import/redefine one library DWG block in a separate structural request.", "INVALID_ARGUMENT");
        var edit = (LibraryBlockDwgEdit)args.Operations[0]; var db = state.Document.Database;
        Need((int)db.Insunits == 4, "Library blocks require a Millimeters target drawing; no unit conversion is performed.", "UNSUPPORTED_UNITS");
        if (edit.Action == "import_block_dwg")
        {
            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead); Need(!table.Has(edit.Name!), "This block name already exists; choose a new name or explicitly redefine its current definition.", "BLOCK_EXISTS");
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead); var zero = (LayerTableRecord)tr.GetObject(layers["0"], OpenMode.ForRead);
            new LayerWriteState(zero.Name, zero.ObjectId == db.Clayer, zero.IsDependent, zero.IsOff, zero.IsFrozen, zero.IsLocked).RequireAttributeWrite();
            return new(null, [], [], [], [], Point3d.Origin, edit.Name!);
        }
        var id = CadStructureWriter.Find(db, edit.DefinitionHandle!); var block = CadBlockDefinitionGeometry388.Definition(id, db, tr); Plane(block.Origin); Independent(block);
        Need(block.Annotative != AnnotativeStates.True, "An annotative target definition requires its owning context route.");
        var members = CadBlockDefinitionGeometry388.Members(block, db, tr, cancel); var attributes = new List<ObjectId>(); var removed = new List<ObjectId>(); var retiredAttributes = new Dictionary<ObjectId, ObjectId[]>();
        foreach (var member in members)
        {
            cancel(); var entity = (Entity)tr.GetObject(member, OpenMode.ForRead);
            if (entity.GetType() == typeof(AttributeDefinition)) attributes.Add(member);
            else
            {
                CadBlockDefinitionMembers392.RequireRetirement(entity, db, tr, cancel); removed.Add(member);
                if (entity is BlockReference reference) retiredAttributes.Add(member, reference.AttributeCollection.Cast<ObjectId>().ToArray());
            }
        }
        var before = CadBlockDefinitionGeometry388.Capture(id, removed.ToHashSet(), db, tr, cancel, true, membershipSelected: true);
        Need(CadBlockDefinitionGeometry388.Token(before) == edit.ExpectedDefinitionToken, "Definition membership, metadata or actual consumers changed; refresh inspect_block.definition_membership.", "STALE_BLOCK_DEFINITION_TOKEN");
        var retained = before.Value.GetProperty("definitions").EnumerateArray().Select(row => CadStructureWriter.Find(db, row.GetProperty("header").GetProperty("root").GetProperty("handle").GetString()!)).ToArray();
        return new(before, removed.ToArray(), retiredAttributes, attributes.ToArray(), retained, block.Origin, block.Name);
    }
    // Queued/native-command admission keeps the exact target checks but does not
    // open another database. Source validation runs once in Execute before any
    // resident mutation. Preview retains the complete read-only source checks.
    internal static void Admit(StructureWriteArguments args, DocumentState state, Transaction tr, Action cancel) => _ = ResolveTarget(args, state, tr, cancel);
    internal static void Preflight(StructureWriteArguments args, DocumentState state, Transaction tr, Action cancel, Action<string>? checkpoint = null)
    { var target = ResolveTarget(args, state, tr, cancel); using var source = new Source((LibraryBlockDwgEdit)args.Operations[0], target.Before == null ? null : target.Origin, cancel, checkpoint); ProjectedReceipt(args, target, source, state.Document.Database, tr, cancel); }

    static void ProjectedReceipt(StructureWriteArguments args, Target target, Source source, Database db, Transaction tr, Action cancel)
    {
        // Redefinition retains the target ATTDEFs, so the existing insertion
        // guard must consider those together with incoming root geometry.
        Need(target.AttributeDefinitions.Length == 0 || !source.Roots.Any(id => source.Transaction.GetObject(id, OpenMode.ForRead) is Spline { Closed: true }),
            "Closed spline block members require an attribute-free definition; redefinition retains the target ATTDEFs.", "UNSUPPORTED_BLOCK_CONTENT");
        EntityRef Dummy() => new(args.BridgeId, args.DocumentId, "FFFFFFFFFFFFFFFF");
        var children = source.Roots.Select((_, i) => new StructureChild(Dummy(), "imported_member", i, null))
            .Concat(source.Roots.SelectMany((root, i) => source.Entities[root].Attributes.Select(id => new StructureChild(Dummy(), "imported_nested_attribute", i, ((AttributeReference)source.Transaction.GetObject(id, OpenMode.ForRead)).Tag))))
            .Concat(target.Removed.Select((_, i) => new StructureChild(Dummy(), "retired_member", i, null)))
            .Concat(target.Removed.SelectMany((root, i) => target.RetiredAttributes.TryGetValue(root, out var attributes) ? attributes.Select(id => new StructureChild(Dummy(), "retired_nested_attribute", i, ((AttributeReference)tr.GetObject(id, OpenMode.ForRead)).Tag)) : Enumerable.Empty<StructureChild>())).ToArray();
        int nameLength = SymbolUtilityServices.GetMaxSymbolNameLength(true, SymbolUtilityServices.IsCompatibilityMode(db));
        Need(nameLength > 0, "The target SDK did not report a positive native symbol-name capacity.");
        // The actual SDK UTF16 capacity bounds native collision-generated names.
        // U+FFFF charges the maximum JSON escape bytes per UTF16 code unit.
        Need(nameLength <= Wire.PayloadBudget / 6, "Native symbol-name capacity exceeds the complete receipt byte budget.", "RESPONSE_LIMIT_EXCEEDED");
        string projectedName = new('\uFFFF', nameLength);
        var projectedStamps = source.Entities.Values.Where(row => source.Transaction.GetObject(row.Id, OpenMode.ForRead).GetType() == typeof(BlockReference) &&
            !LibraryBlockCreationTime.ContainsApplication(row.Metadata.GetProperty("xdata"))).Select(row => new BlockLibraryNativeCreationTime(Id(row.Id), Dummy(), Dummy(), Dummy(),
                "FFFFFFFFFFFFFFFF", Wire.Element(new object[] { new { code = 1001, value = LibraryBlockCreationTime.Application }, new { code = 1071, value = int.MinValue }, new { code = 1071, value = int.MinValue } }),
                true, CadLibraryCreationTime.Corroboration)).ToArray();
        var output = new ModifiedStructure(args.OperationId, "committed", false, [new(0, ((LibraryBlockDwgEdit)args.Operations[0]).Operation, Dummy(), "block_definition", target.Name, target.Before == null, children)], "not_guaranteed",
            block_library_results: [new(source.Path,source.Sha256,Point(source.Base),Point(target.Before==null?source.Base:target.Origin),source.Roots.Length,
                source.ReceiptSymbols.Select(symbol=>new BlockLibrarySymbolMapping(Id(symbol.Key),symbol.Value,Dummy(),projectedName)).ToArray(),
                target.AttributeDefinitions.Select(_=>Dummy()).ToArray(),(target.Before?.References??[]).Select(_=>Dummy()).ToArray())
                {native_creation_times=projectedStamps.Length==0?null:projectedStamps}]);
        SerializedCapacity357.Check(output, Wire.PayloadBudget - 8192, cancel);
    }

    // Rewrite only actual identifier fields, never arbitrary strings that could
    // coincidentally equal a handle (literal text is not an identity alias).
    static JsonElement Mapped(JsonElement value, IReadOnlyDictionary<string, string> ids, IReadOnlyDictionary<string, string>? applications = null, bool metadata = false)
    {
        var node = JsonNode.Parse(value.GetRawText())!;
        void Visit(JsonNode? current)
        {
            if (current is JsonArray array) { foreach (var item in array) Visit(item); return; }
            if (current is not JsonObject row) return;
            if (row["common"] is JsonObject common) common.Remove("linetype");
            foreach (var pair in row.ToArray())
            {
                bool metadataId = metadata && (pair.Key == "extension" && row["owned"] is JsonArray ||
                    pair.Key is "handle" or "owner_handle" or "extension_handle" && row["runtime_type"] != null);
                if ((pair.Key is "style" or "text_style" or "definition" or "layer" or "linetype" or "material" or "plot_id" || metadataId) && pair.Value is JsonValue scalar && scalar.TryGetValue<string>(out var handle))
                { Need(ids.TryGetValue(handle, out var mapped), "The native clone omitted a referenced source identifier mapping.", Mismatch); row[pair.Key] = mapped; }
                else Visit(pair.Value);
            }
            if (row["code"] is JsonValue code && code.TryGetValue<int>(out int group) && group == 1005)
            {
                var translated = LibraryBlockMetadataReferences.MapHandleReference(Wire.Element(row), ids);
                row["value"] = JsonNode.Parse(translated.GetProperty("value").GetRawText());
            }
            if (row["code"] is JsonValue appCode && appCode.TryGetValue<int>(out int appGroup) && appGroup == 1001 && applications != null)
            {
                var translated = LibraryBlockMetadataReferences.MapApplicationReference(Wire.Element(row), applications);
                if (translated.TryGetProperty("value", out var appValue)) row["value"] = JsonNode.Parse(appValue.GetRawText());
            }
            if (row["native_type"]?.GetValue<string>() == "ObjectId" && row["handle"] is JsonValue raw && raw.TryGetValue<string>(out var objectHandle))
            { Need(ids.TryGetValue(objectHandle, out var mapped), "Native XData omitted a referenced object identifier mapping.", Mismatch); row["handle"] = mapped; }
            if (row["bridge_instance_id"] != null && row["document_id"] != null && row["handle"] is JsonValue nativeHandle && nativeHandle.TryGetValue<string>(out var actualId))
            { Need(ids.TryGetValue(actualId, out var mapped), "A native multiline style reference mapping is unavailable.", Mismatch); row["handle"] = mapped; }
        }
        Visit(node); return Wire.Element(node);
    }
    static JsonElement Comparable(JsonElement value)
    {
        var node = JsonNode.Parse(value.GetRawText())!;
        void Visit(JsonNode? item)
        {
            if (item is JsonArray array) { foreach (var child in array) Visit(child); }
            else if (item is JsonObject row)
            {
                if (row["common"] is JsonObject common) common.Remove("linetype");
                if (row["code"] is JsonValue code && code.TryGetValue<int>(out int group) && group == 1005)
                {
                    var canonical = LibraryBlockMetadataReferences.MapHandleReference(Wire.Element(row));
                    row["value"] = JsonNode.Parse(canonical.GetProperty("value").GetRawText());
                }
                foreach (var child in row) Visit(child.Value);
            }
        }
        Visit(node); return Wire.Element(node);
    }
    internal static Result Execute(StructureWriteArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        Transaction? transaction = null; Source? source = null; BlockTableRecord? created = null; bool started = false, commitAttempted = false; string stage = "resolve";
        try
        {
            void Checkpoint(string phase)
            {
                try
                {
                    log.Write(job.Operation, job.OriginalRequestId, "library_write_checkpoint", transaction: JsonSerializer.Serialize(new
                    {
                        phase,
                        expected_revision = args.ExpectedRevision,
                        current_revision = state.Revision
                    }, Wire.Json), operationId: job.OperationId);
                }
                catch (System.Exception) {/* Diagnostics cannot alter native execution. */}
            }
            void Context()
            {
                Need(ReferenceEquals(Application.DocumentManager.MdiActiveDocument, state.Document), "The original target document is no longer active.", "DOCUMENT_NOT_ACTIVE");
                Need(!state.Document.IsReadOnly, "The original target document became read-only.", "DOCUMENT_READ_ONLY");
            }
            Context(); var db = state.Document.Database; var edit = (LibraryBlockDwgEdit)args.Operations[0]; transaction = db.TransactionManager.StartTransaction(); var admitted = ResolveTarget(args, state, transaction, job.CheckCancellation);
            stage = "source_validation"; Checkpoint("source_validation_before");
            source = new(edit, admitted.Before == null ? null : admitted.Origin, job.CheckCancellation, Checkpoint); Checkpoint("source_validation_after");
            // The source read remains in this single authorized command. Never
            // rebase the caller's revision; verify the actual target again using
            // its original name/token, identities, layers and ownership guards.
            Context(); stage = "target_revalidation"; Checkpoint("target_revalidation_before"); var target = ResolveTarget(args, state, transaction, job.CheckCancellation);
            Need(target.Before?.Definition == admitted.Before?.Definition && target.Name == admitted.Name && target.Origin.X == admitted.Origin.X && target.Origin.Y == admitted.Origin.Y && target.Origin.Z == admitted.Origin.Z,
                "The target definition identity/name/base changed during source validation.", "STALE_BLOCK_DEFINITION_TOKEN");
            ProjectedReceipt(args, target, source, db, transaction, job.CheckCancellation); Context(); Checkpoint("target_revalidation_after");
            var originalDefinitions = target.RetainedDefinitions.Select(Id).ToHashSet(StringComparer.OrdinalIgnoreCase); var changed = target.Removed.Select(Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            JsonElement? invariant = target.Before == null ? null : CadBlockDefinitionMembers392.Retained(target.Before, changed, originalDefinitions);
            var layoutMarkers = LayoutBookendWitnesses(db, transaction, job.CheckCancellation);
            var retainedMarkers = (target.Before == null ? Array.Empty<Storage>() : Bookends((BlockTableRecord)transaction.GetObject(target.Before.Definition, OpenMode.ForRead), db, transaction, job.CheckCancellation))
                .Concat(layoutMarkers).ToArray();
            var symbolWitnesses = ExistingSymbolWitnesses(source, db, transaction, job.CheckCancellation);
            EntityRef Ref(ObjectId id) => new(args.BridgeId, args.DocumentId, Id(id)); var children = new List<StructureChild>();
            job.CheckCancellation(); stage = "native_definition"; started = true; log.Write(job.Operation, job.OriginalRequestId, "block_library_started", operationId: job.OperationId);
            ObjectId definitionId;
            if (target.Before == null)
            {
                var table = (BlockTable)transaction.GetObject(db.BlockTableId, OpenMode.ForWrite); created = new() { Name = target.Name, Origin = source.Base, Units = UnitsValue.Millimeters };
                definitionId = table.Add(created); transaction.AddNewlyCreatedDBObject(created, true);
            }
            else definitionId = target.Before.Definition;
            stage = "native_wblock_clone"; using var roots = new ObjectIdCollection(source.Roots); using var mapping = new IdMapping();
            source.Database.WblockCloneObjects(roots, definitionId, mapping, DuplicateRecordCloning.MangleName, false); job.CheckCancellation();
            var map = new Dictionary<ObjectId, ObjectId>(); var handles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var applications = new Dictionary<string, string>(StringComparer.Ordinal);
            var renamed = new List<BlockLibrarySymbolMapping>(); var rows = new List<(IdPair Pair, DBObject Source, DBObject Target)>();
            var nativeCreationTimes = new Dictionary<ObjectId, BlockLibraryNativeCreationTime>();
            foreach (IdPair pair in mapping)
            {
                job.CheckCancellation(); Need(Live(pair.Key) && pair.Key.Database?.UnmanagedObject == source.Database.UnmanagedObject && Live(pair.Value) && pair.Value.Database?.UnmanagedObject == db.UnmanagedObject, "A native clone mapping has foreign/unavailable source or destination identity.", Mismatch);
                map.Add(pair.Key, pair.Value); handles.Add(Id(pair.Key), Id(pair.Value));
                var original = source.Transaction.GetObject(pair.Key, OpenMode.ForRead); var actual = transaction.GetObject(pair.Value, OpenMode.ForRead); rows.Add((pair, original, actual));
                if (original is RegAppTableRecord oldApp && actual is RegAppTableRecord newApp) applications.Add(oldApp.Name, newApp.Name);
            }
            ObjectId M(ObjectId id) { Need(map.TryGetValue(id, out var result), "The native clone omitted a required source object mapping.", Mismatch); return result; }
            object PairDetails((IdPair Pair, DBObject Source, DBObject Target) row, string role) => new
            {
                classification = role,
                source_handle = Id(row.Pair.Key),
                source_type = row.Source.GetType().FullName,
                source_owner = MaybeId(row.Source.OwnerId),
                target_handle = Id(row.Pair.Value),
                target_type = row.Target.GetType().FullName,
                target_owner = MaybeId(row.Target.OwnerId),
                source_native_class = ClassIdentity(row.Source, row.Source.GetType() == typeof(RegAppTableRecord) ? typeof(RegAppTableRecord) : StorageType(role)).Observation,
                target_native_class = ClassIdentity(row.Target, row.Source.GetType() == typeof(RegAppTableRecord) ? typeof(RegAppTableRecord) : StorageType(role)).Observation,
                source_symbol = SymbolEvidence(row.Source, source.Transaction, job.CheckCancellation),
                target_symbol = SymbolEvidence(row.Target, transaction, job.CheckCancellation),
                is_cloned = row.Pair.IsCloned,
                is_primary = row.Pair.IsPrimary,
                is_owner_translated = row.Pair.IsOwnerTranslated,
                source_known_entity = source.Entities.ContainsKey(row.Pair.Key),
                source_known_definition = source.Definitions.ContainsKey(row.Pair.Key),
                source_known_storage = source.Storages.ContainsKey(row.Pair.Key),
                source_model_space = row.Pair.Key == source.ModelSpace,
                source_consumed_mapping = source.ConsumesMapping(row.Pair.Key)
            };
            void MappingNeed(bool condition, string message, (IdPair Pair, DBObject Source, DBObject Target) row, string role)
            { if (!condition) throw new DomainException(Mismatch, message, false, PairDetails(row, role)); }
            var terminatorOwners = new HashSet<ObjectId>();
            stage = "native_storage_mapping";
            // A terminator is not exposed by the managed attribute iterator.
            // Discover only an exact native source->target map pair, never a
            // class/owner scan, guessed handle or a non-primary exemption.
            foreach (var row in rows.Where(row => row.Source is Entity && !source.Entities.ContainsKey(row.Pair.Key) && !source.Storages.ContainsKey(row.Pair.Key)))
            {
                if (!ClassIdentity(row.Source, typeof(SequenceEnd)).Exact) continue;
                job.CheckCancellation(); var owner = row.Source.OwnerId;
                MappingNeed(source.Entities.TryGetValue(owner, out var admittedOwner) && admittedOwner.Definition != null && map.ContainsKey(owner) && row.Target is Entity &&
                    ClassIdentity(row.Target, typeof(SequenceEnd)).Exact && row.Target.OwnerId == M(owner),
                    "A mapped sequence terminator has no exact admitted source/mapped target occurrence owner.", row, "sequence_end");
                var oldOwner = source.Transaction.GetObject(owner, OpenMode.ForRead); var newOwner = transaction.GetObject(M(owner), OpenMode.ForRead);
                MappingNeed((oldOwner.GetType() == typeof(BlockReference) || oldOwner.GetType() == typeof(MInsertBlock)) && newOwner.GetType() == oldOwner.GetType() &&
                    newOwner is BlockReference reference && reference.BlockTableRecord == M(admittedOwner!.Definition!.Value) && terminatorOwners.Add(owner),
                    "A mapped terminator has a changed occurrence type/definition or repeated actual owner.", row, "sequence_end");
                try { source.AddStorage(StorageState((Entity)row.Source, owner, "sequence_end", source.Database, source.Transaction, job.CheckCancellation)); }
                catch (DomainException error)
                { throw new DomainException(Mismatch, "The mapped native terminator data could not be completely verified.", false, new { mapping = PairDetails(row, "sequence_end"), cause = error.Error }); }
            }
            source.ValidateStorageReferences();
            foreach (var row in rows)
            {
                job.CheckCancellation(); var pair = row.Pair;
                bool container = pair.Key == source.ModelSpace && pair.Value == definitionId && row.Source.GetType() == typeof(BlockTableRecord) && row.Target.GetType() == typeof(BlockTableRecord);
                bool layoutContext = !pair.IsCloned && !pair.IsPrimary && LayoutContext(row.Source, source.Database, source.Transaction, out var sourceKind) &&
                    LayoutContext(row.Target, db, transaction, out var targetKind) && sourceKind == targetKind;
                if (layoutContext && pair.Key != source.ModelSpace && !source.Definitions.ContainsKey(pair.Key) && !source.ReceiptSymbols.ContainsKey(pair.Key))
                {
                    // Unconsumed native layout context is not imported data and
                    // cannot authorize a later semantic identifier translation.
                    map.Remove(pair.Key); handles.Remove(Id(pair.Key)); continue;
                }
                bool layoutBookend = !pair.IsCloned && !pair.IsPrimary && !source.Storages.ContainsKey(pair.Key) &&
                    row.Source is Entity && row.Target is Entity &&
                    Live(row.Source.OwnerId) && Live(row.Target.OwnerId);
                if (layoutBookend)
                {
                    var oldOwner = source.Transaction.GetObject(row.Source.OwnerId, OpenMode.ForRead); var newOwner = transaction.GetObject(row.Target.OwnerId, OpenMode.ForRead);
                    bool begin = ClassIdentity(row.Source, typeof(BlockBegin)).Exact && ClassIdentity(row.Target, typeof(BlockBegin)).Exact;
                    bool end = !begin && ClassIdentity(row.Source, typeof(BlockEnd)).Exact && ClassIdentity(row.Target, typeof(BlockEnd)).Exact;
                    layoutBookend = (begin || end) && LayoutContext(oldOwner, source.Database, source.Transaction, out var oldKind) && LayoutContext(newOwner, db, transaction, out var newKind) && oldKind == newKind &&
                        (begin ? ((BlockTableRecord)oldOwner).BlockBeginId : ((BlockTableRecord)oldOwner).BlockEndId) == pair.Key &&
                        (begin ? ((BlockTableRecord)newOwner).BlockBeginId : ((BlockTableRecord)newOwner).BlockEndId) == pair.Value;
                }
                if (layoutBookend) { map.Remove(pair.Key); handles.Remove(Id(pair.Key)); continue; }
                bool auxiliary = !pair.IsCloned && !pair.IsPrimary && !source.ConsumesMapping(pair.Key) &&
                    row.Source is SymbolTableRecord auxiliarySource && row.Target is SymbolTableRecord auxiliaryTarget && auxiliarySource.GetType() == auxiliaryTarget.GetType() &&
                    OrdinarySymbolIndex(auxiliarySource, source.Database, source.Transaction) && OrdinarySymbolIndex(auxiliaryTarget, db, transaction);
                if (auxiliary)
                {
                    // IdMapping can contain native prepopulated context rows
                    // outside the requested graph. They are not imported data.
                    // Exclude their keys from every identity translator: an
                    // actual later geometry/metadata consumer must still fail.
                    if (row.Source is RegAppTableRecord)
                    {
                        // Actual name/metadata are evidence, never an admission
                        // whitelist. Optional logging cannot affect the clone.
                        try
                        {
                            log.Write(job.Operation, job.OriginalRequestId, "library_auxiliary_regapp_context",
                            transaction: JsonSerializer.Serialize(PairDetails(row, "unconsumed_registered_application_context"), Wire.Json), operationId: job.OperationId);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (System.Exception) { }
                    }
                    LibraryBlockMetadataReferences.ExcludeAuxiliaryCorrespondence(map, handles, applications, pair.Key, Id(pair.Key), (row.Source as RegAppTableRecord)?.Name);
                    continue;
                }
                MappingNeed(row.Source is not Entity || source.Entities.ContainsKey(pair.Key) || source.Storages.ContainsKey(pair.Key),
                    "Native cloning followed an unrequested geometry dependency outside the supported source graph.", row, "unclassified_geometry");
                MappingNeed(row.Source is not BlockTableRecord || source.Definitions.ContainsKey(pair.Key) || container || layoutContext,
                    "Native cloning followed an unrequested definition dependency outside the supported source graph.", row, "definition_or_model_space_owner");
                MappingNeed(row.Source is not SymbolTableRecord || source.ReceiptSymbols.ContainsKey(pair.Key) || container || layoutContext,
                    "Native cloning followed an unrequested named-symbol dependency outside the supported source graph.", row, "unclassified_symbol");
                if (!container && pair.IsCloned && row.Source is SymbolTableRecord oldSymbol && row.Target is SymbolTableRecord newSymbol && oldSymbol.Name != newSymbol.Name)
                    renamed.Add(new(Id(pair.Key), oldSymbol.Name, Ref(pair.Value), newSymbol.Name));
            }
            bool storageClassEvidenceLogged = false;
            void VerifyStorage(Transaction read)
            {
                object? sequenceEndEvidence = null; int sequenceEndCount = 0;
                foreach (var owner in layoutMarkers.Select(marker => marker.Owner).Distinct())
                { job.CheckCancellation(); _ = CadBlockSpace.RequireSpace(db, read, owner); }
                foreach (var storage in source.Storages.Values)
                {
                    if (!map.ContainsKey(storage.Id))
                    {
                        // The ModelSpace container itself is not requested
                        // geometry. Nested copied definitions must retain their
                        // actual marker correspondence and user data.
                        if (storage.Owner == source.ModelSpace) continue;
                        throw new DomainException(Mismatch, "The native clone omitted a required copied-definition storage marker mapping.", false,
                            new { source_handle = Id(storage.Id), source_owner = Id(storage.Owner), classification = storage.Role });
                    }
                    job.CheckCancellation(); var id = M(storage.Id); var pair = mapping.Lookup(storage.Id); var original = source.Transaction.GetObject(storage.Id, OpenMode.ForRead); var actual = read.GetObject(id, OpenMode.ForRead);
                    var row = (Pair: pair, Source: original, Target: actual); ObjectId owner;
                    if (storage.Owner == source.ModelSpace)
                    {
                        // Source container bookends can be mapped to native
                        // context storage or the explicit destination owner.
                        // Neither relation aliases the actual IdMapping pair.
                        owner = actual.OwnerId; bool destination = owner == definitionId;
                        bool contextMarker = Live(owner) && LayoutContext(read.GetObject(owner, OpenMode.ForRead), db, read, out var kind) && kind == "model" && !pair.IsCloned && !pair.IsPrimary;
                        MappingNeed(destination || contextMarker, "A source ModelSpace marker has neither the explicit destination nor a proven native model context owner.", row, storage.Role);
                    }
                    else owner = M(storage.Owner);
                    bool nativeType = ClassIdentity(original, StorageType(storage.Role)).Exact && ClassIdentity(actual, StorageType(storage.Role)).Exact;
                    MappingNeed(nativeType && actual is Entity && actual.OwnerId == owner, "A mapped storage marker changed native type or exact owner.", row, storage.Role);
                    if (storage.Role == "sequence_end") { sequenceEndCount++; sequenceEndEvidence ??= PairDetails(row, storage.Role); }
                    if (storage.Role is "block_begin" or "block_end")
                    {
                        var block = (BlockTableRecord)read.GetObject(owner, OpenMode.ForRead);
                        MappingNeed((storage.Role == "block_begin" ? block.BlockBeginId : block.BlockEndId) == id, "A mapped bookend differs from its actual destination owner's native getter.", row, storage.Role);
                    }
                    if (storage.Owner == source.ModelSpace) continue; // owner correspondence, not imported container data
                    var observed = StorageState((Entity)actual, owner, storage.Role, db, read, job.CheckCancellation);
                    MappingNeed(BlockMoveGeometry.SameProperties(Mapped(storage.Common, handles, applications), Comparable(observed.Common)),
                        "Native storage marker common data differs from its actual mapped source.", row, storage.Role);
                    SameMetadata(Mapped(storage.Metadata, handles, applications, metadata: true), Comparable(observed.Metadata), original, actual, storage.Role + "_metadata",
                        "Native storage marker user metadata differs from its actual mapped source.");
                }
                foreach (var stored in retainedMarkers)
                {
                    var actual = (Entity)read.GetObject(stored.Id, OpenMode.ForRead); var observed = StorageState(actual, stored.Owner, stored.Role, db, read, job.CheckCancellation);
                    var owner = (BlockTableRecord)read.GetObject(stored.Owner, OpenMode.ForRead);
                    Need((stored.Role == "block_begin" ? owner.BlockBeginId : owner.BlockEndId) == stored.Id, "Redefinition replaced a retained native target bookend.", Mismatch);
                    Exact(stored.Common, observed.Common, "Redefinition changed retained target marker data.");
                    SameMetadata(stored.Metadata, observed.Metadata, actual, actual, "retained_target_storage_metadata",
                        "Redefinition changed retained target marker metadata.", "Retained target metadata snapshot versus the current target object.");
                }
                if (!storageClassEvidenceLogged && sequenceEndEvidence != null)
                {
                    try
                    {
                        log.Write(job.Operation, job.OriginalRequestId, "library_storage_native_class_verified", transaction: JsonSerializer.Serialize(new
                        {
                            verification_phase = "post_clone_storage_mapping",
                            verified_sequence_end_count_this_pass = sequenceEndCount,
                            evidence_logged_once = true,
                            evidence_sample_only = true,
                            sample = sequenceEndEvidence
                        }, Wire.Json), operationId: job.OperationId);
                    }
                    catch (System.Exception) {/* Diagnostics cannot alter native verification. */}
                    storageClassEvidenceLogged = true;
                }
            }
            VerifyStorage(transaction);
            var imported = source.Roots.Select(M).ToArray(); var offset = (target.Before == null ? source.Base : target.Origin) - source.Base;
            stage = "native_root_translation";
            if (offset.Length > 0) foreach (var id in imported)
            {
                job.CheckCancellation(); var entity = (Entity)transaction.GetObject(id, OpenMode.ForWrite);
                // Resident native occurrence transforms own their attributes;
                // those attributes must not be transformed a second time.
                entity.TransformBy(Matrix3d.Displacement(offset));
            }
            stage = "native_retirement";
            foreach (var id in target.Removed)
            {
                job.CheckCancellation(); var entity = (Entity)transaction.GetObject(id, OpenMode.ForWrite); CadBlockDefinitionMembers392.RequireRetirement(entity, db, transaction, job.CheckCancellation);
                if (entity is BlockReference reference) foreach (ObjectId attribute in reference.AttributeCollection) children.Add(new(Ref(attribute), "retired_nested_attribute", Array.IndexOf(target.Removed, id), ((AttributeReference)transaction.GetObject(attribute, OpenMode.ForRead)).Tag));
                entity.Erase(true); children.Add(new(Ref(id), "retired_member", Array.IndexOf(target.Removed, id), null));
            }
            foreach (var id in imported) changed.Add(Id(id));
            foreach (var id in target.Before?.References ?? []) { job.CheckCancellation(); ((Entity)transaction.GetObject(id, OpenMode.ForWrite)).RecordGraphicsModified(true); }
            void Readback(Transaction read)
            {
                var definition = CadBlockDefinitionGeometry388.Definition(definitionId, db, read); Plane(definition.Origin);
                var expectedOrigin = target.Before == null ? source.Base : target.Origin;
                Need(definition.Name == target.Name && definition.Origin.X == expectedOrigin.X && definition.Origin.Y == expectedOrigin.Y && definition.Origin.Z == expectedOrigin.Z, "The destination definition identity/name/base changed.", Mismatch);
                var expectedMembership = target.AttributeDefinitions.Concat(imported).ToArray();
                Need(CadBlockDefinitionGeometry388.Members(definition, db, read, job.CheckCancellation).SequenceEqual(expectedMembership), "The destination membership differs from retained ATTDEFs plus mapped source roots.", Mismatch);
                foreach (var row in source.Entities.Values)
                {
                    job.CheckCancellation(); var id = M(row.Id); Need(mapping.Contains(row.Id) && mapping.Lookup(row.Id).IsCloned, "A requested source entity was reused instead of cloned.", Mismatch);
                    var actual = read.GetObject(id, OpenMode.ForRead) as Entity; var original = (Entity)source.Transaction.GetObject(row.Id, OpenMode.ForRead);
                    Need(actual != null && actual.GetType() == original.GetType() && actual.OwnerId == (source.Roots.Contains(row.Id) ? definitionId : M(row.Owner)), "A mapped entity changed type or native owner.", Mismatch);
                    Same(Mapped(row.Expected, handles, applications), Comparable(Snapshot(actual, read, job.CheckCancellation, new Vector3d(0, 0, 0))), "Mapped native geometry, display or owned attribute values differ from the source.");
                    var expectedMetadata = Mapped(row.Metadata, handles, applications, metadata: true);
                    var actualMetadata = Metadata(actual, read, job.CheckCancellation); var comparableMetadata = Comparable(actualMetadata);
                    BlockLibraryNativeCreationTime? creationTime = null;
                    if (nativeCreationTimes.ContainsKey(id) || !NativeMetadataEquality.Same(expectedMetadata, comparableMetadata) &&
                        !LibraryBlockCreationTime.ContainsApplication(row.Metadata.GetProperty("xdata")) && LibraryBlockCreationTime.ContainsApplication(actualMetadata.GetProperty("xdata")))
                    {
                        Need(actual.GetType() == typeof(BlockReference), "A newly added creation-time field needs its exact supported native block-reference adapter.", Mismatch);
                        var witnessed = CadLibraryCreationTime.Read(args.BridgeId, args.DocumentId, Id(row.Id), row.Metadata.GetProperty("xdata"), (BlockReference)actual, actualMetadata,
                            state, read, job.CheckCancellation, item => Metadata(item, read, job.CheckCancellation));
                        creationTime = witnessed.Receipt;
                        if (commitAttempted && !nativeCreationTimes.ContainsKey(id))
                            throw new DomainException(Mismatch, "A newly added native creation-time field was not observed before commit.", false, new { source_handle = Id(row.Id), target_handle = Id(id), native_creation_time = creationTime });
                        try
                        {
                            var expectedNode = JsonNode.Parse(expectedMetadata.GetRawText())!.AsObject();
                            expectedNode["xdata"] = JsonNode.Parse(LibraryBlockCreationTime.ExpectedXData(expectedMetadata.GetProperty("xdata"), witnessed.Stamp).GetRawText());
                            expectedMetadata = Wire.Element(expectedNode);
                        }
                        catch (DomainException error)
                        {
                            throw new DomainException(Mismatch, error.Error.message, false, new
                            {
                                source_handle = Id(row.Id),
                                target_handle = Id(id),
                                native_creation_time = creationTime,
                                first_difference = LibraryBlockMetadataComparison.FirstDifference(expectedMetadata, comparableMetadata),
                                cause = error.Error
                            });
                        }
                        if (nativeCreationTimes.TryGetValue(id, out var previous))
                            SameMetadata(Wire.Element(previous), Wire.Element(creationTime), original, actual, "native_creation_time_stability",
                                "The exact newly added native creation-time field changed across readbacks.", "Per-operation native creation-time witness for the same actual target ObjectId.", creationTime);
                    }
                    SameMetadata(expectedMetadata, comparableMetadata, original, actual, "entity_metadata",
                        "Native cloned entity user metadata differs from its actual mapped source.", nativeCreationTime: creationTime);
                    if (creationTime != null)
                    {
                        bool firstWitness = !nativeCreationTimes.ContainsKey(id); nativeCreationTimes[id] = creationTime;
                        if (firstWitness) try
                            {
                                log.Write(job.Operation, job.OriginalRequestId, "library_native_creation_time_verified",
                            transaction: JsonSerializer.Serialize(new { verification_phase = stage, creation_time = creationTime }, Wire.Json), operationId: job.OperationId);
                            }
                            catch (System.Exception) {/* Diagnostics cannot alter native verification. */}
                    }
                    if (actual is BlockReference reference) Need(reference.BlockTableRecord == M(row.Definition!.Value) && reference.AttributeCollection.Cast<ObjectId>().SequenceEqual(row.Attributes.Select(M)), "A cloned nested reference has different actual definition/attribute identities.", Mismatch);
                }
                foreach (var row in source.Definitions.Values)
                {
                    job.CheckCancellation(); var actual = CadBlockDefinitionGeometry388.Definition(M(row.Id), db, read, false);
                    Need(mapping.Lookup(row.Id).IsCloned && CadBlockDefinitionGeometry388.Members(actual, db, read, job.CheckCancellation).SequenceEqual(row.Members.Select(M)), "A nested source definition was reused or its mapped membership changed.", Mismatch);
                    Same(row.Value, DefinitionValue(actual), "A cloned nested definition base/units/scaling changed.");
                    SameMetadata(Mapped(row.Metadata, handles, applications, metadata: true), Comparable(Metadata(actual, read, job.CheckCancellation)),
                        source.Transaction.GetObject(row.Id, OpenMode.ForRead), actual, "definition_metadata",
                        "Native cloned definition user metadata differs from its actual mapped source.");
                }
                foreach (var row in source.Symbols.Values)
                {
                    job.CheckCancellation(); Need(read.GetObject(M(row.Id), OpenMode.ForRead) is SymbolTableRecord, "A referenced source symbol did not map to a native target symbol.", Mismatch);
                    var symbol = (SymbolTableRecord)read.GetObject(M(row.Id), OpenMode.ForRead);
                    Same(Mapped(row.Value, handles, applications), Comparable(SymbolValue(symbol, job.CheckCancellation)), "A mapped source symbol has different native appearance semantics.");
                    if (!mapping.Lookup(row.Id).IsCloned)
                    {
                        Need(symbolWitnesses.TryGetValue(symbol.ObjectId, out var witness), "A reused target symbol has no exact pre-clone user-data witness.", Mismatch);
                        Need(symbol.OwnerId == witness.Owner && symbol.Name == witness.Name && symbol.GetType().FullName == witness.RuntimeType,
                            "Native symbol reuse changed the actual preexisting target identity, type, owner or name.", Mismatch);
                        SameMetadata(witness.Metadata, Metadata(symbol, read, job.CheckCancellation), symbol, symbol, "retained_target_symbol_metadata",
                            "Native symbol reuse changed retained target-only user metadata.", "Pre-clone target-only metadata snapshot versus the current reused target symbol.");
                    }
                    // Native reserved-symbol reuse does not clone an empty
                    // incoming payload over existing target-only user data.
                    if (mapping.Lookup(row.Id).IsCloned || HasMetadataPayload(row.Metadata))
                        SameMetadata(Mapped(row.Metadata, handles, applications, metadata: true), Comparable(Metadata(symbol, read, job.CheckCancellation)),
                            source.Transaction.GetObject(row.Id, OpenMode.ForRead), symbol, "symbol_metadata",
                            "Native cloned/referenced symbol user metadata differs from its actual mapped source.");
                }
                VerifyStorage(read);
                foreach (var id in target.Removed)
                {
                    job.CheckCancellation(); Need(id.IsErased || id.IsEffectivelyErased, "A replaced target member remains live.", Mismatch);
                    Need(read.GetObject(id, OpenMode.ForRead, true).OwnerId == definitionId, "A retired target member changed owner.", Mismatch);
                }
                foreach (var pair in target.RetiredAttributes) foreach (var id in pair.Value)
                {
                    job.CheckCancellation(); Need(id.IsErased || id.IsEffectivelyErased, "A retired nested occurrence retained a live owned attribute.", Mismatch);
                    Need(read.GetObject(id, OpenMode.ForRead, true) is AttributeReference attribute && attribute.OwnerId == pair.Key, "A retired nested attribute changed native owner/type.", Mismatch);
                }
                if (target.Before != null)
                {
                    var after = CadBlockDefinitionGeometry388.Capture(definitionId, [], db, read, job.CheckCancellation, true, membershipSelected: true, retainedDefinitions: target.RetainedDefinitions);
                    SameMetadata(invariant!.Value, CadBlockDefinitionMembers392.Retained(after, changed, originalDefinitions), definition, definition, "retained_target_graph",
                        "Redefinition changed a retained definition/ATTDEF/consumer/occurrence attribute.",
                        "Retained target graph snapshot versus the current target graph; geometry numbers and metadata integer lexemes remain exact.");
                }
                else
                {
                    // Reuse the actual insertion admission route on the actual
                    // imported definition. Resolve constructs a detached probe;
                    // it neither creates nor transforms a resident occurrence.
                    var table = (BlockTable)read.GetObject(db.BlockTableId, OpenMode.ForRead);
                    LayerWriteState Layer(ObjectId id)
                    { var layer = (LayerTableRecord)read.GetObject(id, OpenMode.ForRead); return new(layer.Name, id == db.Clayer, layer.IsDependent, layer.IsOff, layer.IsFrozen, layer.IsLocked); }
                    _ = CadBlockWriter.Resolve(new InsertBlockEdit(Id(definitionId), new(0, 0), "0", 0, 1, new(StringComparer.Ordinal)), db, read, table[BlockTableRecord.ModelSpace], Layer, job.CheckCancellation, warning => source.Warnings.Add(warning));
                }
                source.Verify();
            }
            stage = "native_readback"; Readback(transaction);
            foreach (var root in source.Roots) children.Add(new(Ref(M(root)), "imported_member", Array.IndexOf(source.Roots, root), null));
            foreach (var root in source.Roots) foreach (var attribute in source.Entities[root].Attributes) children.Add(new(Ref(M(attribute)), "imported_nested_attribute", Array.IndexOf(source.Roots, root), ((AttributeReference)source.Transaction.GetObject(attribute, OpenMode.ForRead)).Tag));
            var receipt = new BlockLibraryDwgResult(source.Path, source.Sha256, Point(source.Base), Point(target.Before == null ? source.Base : target.Origin), source.Roots.Length, renamed.ToArray(), target.AttributeDefinitions.Select(Ref).ToArray(), (target.Before?.References ?? []).Select(Ref).ToArray())
            { native_creation_times = nativeCreationTimes.Count == 0 ? null : nativeCreationTimes.Values.ToArray() };
            var data = new ModifiedStructure(args.OperationId, "committed", false, [new(0, edit.Operation, Ref(definitionId), "block_definition", target.Name, target.Before == null, children.ToArray())], "not_guaranteed", block_library_results: [receipt]);
            SerializedCapacity357.Check(data, Wire.PayloadBudget - 8192, job.CheckCancellation); job.CheckCancellation(); stage = "commit"; job.BeginCommit(); commitAttempted = true; transaction.Commit(); transaction.Dispose(); transaction = null;
            stage = "postcommit_readback"; using (var read = db.TransactionManager.StartTransaction()) Readback(read);
            var result = Result.Ok(job.OriginalRequestId, data, context, new[] { Warning, WriteRecovery.Warning }.Concat(source.Warnings).ToArray()); job.RecordCommit(result); return result;
        }
        catch (System.Exception error)
        {
            if (job.CommittedResult is { } committed) return committed; System.Exception? cleanup = null; try { transaction?.Dispose(); } catch (System.Exception caught) { cleanup = caught; }
            string cause = error is DomainException domain ? domain.Error.code : error is OperationCanceledException ? "CANCELLED" : "BLOCK_LIBRARY_FAILED";
            return job.Error(started ? "WRITE_OUTCOME_UNKNOWN" : cause, started ? "Native DWG block cloning/redefinition started, but its complete outcome is unverified; inspect the named definition, target consumers and actual native objects before dependent writes." : error.Message,
                started ? "unknown" : "not_started", context, new { cause, stage, commit_attempted = commitAttempted, message = error.Message, native_details = (error as DomainException)?.Error.details, cleanup_error = cleanup?.GetType().Name }) with
            { warnings = [Warning, WriteRecovery.Warning] };
        }
        finally { try { created?.Dispose(); } finally { source?.Dispose(); } }
    }
}
