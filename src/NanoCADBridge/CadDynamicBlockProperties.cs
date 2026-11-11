using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// SDK database.h:10980-11044; installed hostdbmgd metadata A4DEDB95...77D52E9B.
// Value.set -> oddbObjectToResbuf -> native property.setValue. The selector is
// one current collection ordinal plus its complete snapshot, never stored ID.
internal static class CadDynamicBlockProperties
{
    const string Warning = "DYNAMIC_BLOCK_PROPERTY: The actual native dynamic evaluator changes one existing occurrence. Native parameter values keep their SDK units and coordinate frame without conversion. Dependent property values, visibility, owned attribute geometry and the native anonymous representation may change and are reported. Snapshot indices after regeneration are not persistent property identities and no dependency edge is inferred. No definition authoring, coordinate reconstruction or attribute synchronization is performed. Private evaluation-graph payload and resulting geometry are not fully predicted; inspect the useful result. Undo/rollback is not guaranteed; unknown never means success or rollback.";
    const string Mismatch = "NATIVE_DYNAMIC_BLOCK_MISMATCH";
    const string Zero = "00000000-0000-0000-0000-000000000000";
    internal sealed record State(ObjectId Id, ObjectId Definition, ObjectId Representation, ObjectId Anonymous,
        JsonElement Root, JsonElement DefinitionState, Dictionary<ObjectId, JsonElement> Attributes,
        DynamicBlockPropertiesInventory Inventory);
    sealed record Binding(SetDynamicBlockPropertySpec Edit, State Before);
    static void Need([DoesNotReturnIf(false)] bool condition, string message, string code = "UNSUPPORTED_DYNAMIC_BLOCK_PROPERTY")
    { if (!condition) throw new DomainException(code, message); }
    static bool Live(ObjectId id) => !id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased;
    static string Id(ObjectId id) => Validation.NormalizeHandle(id.Handle.ToString());
    static string? OptionalId(ObjectId id) => id.IsNull ? null : Id(id);
    static bool Same(JsonElement a, JsonElement b) => a.GetRawText() == b.GetRawText();
    static double Finite(double value) { Need(double.IsFinite(value), "A native dynamic state contains a nonfinite number."); return value == 0 ? 0d : value; }
    static double[] Point(Point3d value) => [Finite(value.X), Finite(value.Y), Finite(value.Z)];
    static double[] Vector(Vector3d value) => [Finite(value.X), Finite(value.Y), Finite(value.Z)];
    static double[] Matrix(Matrix3d value)
    { var result = value.ToArray(); foreach (double item in result) Finite(item); return result; }
    internal static bool Handles(ModifyArguments args) => args.Operations.Any(item => item is SetDynamicBlockPropertySpec);

    static DynamicBlockPropertyValue NativeValue(object? value, Database database, out bool complete)
    {
        complete = true;
        return value switch
        {
            double number => new("double", Wire.Element(Finite(number))),
            short number => new("int16", Wire.Element(number)),
            int number => new("int32", Wire.Element(number)),
            string text => new("string", Wire.Element(text)),
            Point2d point => new("point2d", Wire.Element(new[] { Finite(point.X), Finite(point.Y) })),
            Point3d point => new("point3d", Wire.Element(Point(point))),
            ObjectId id => ObjectIdentity(id, database, out complete),
            null => new("null", JsonSerializer.SerializeToElement<object?>(null, Wire.Json)),
            _ => Unavailable(out complete)
        };
        static DynamicBlockPropertyValue Unavailable(out bool observed)
        { observed = false; return new("unsupported", JsonSerializer.SerializeToElement<object?>(null, Wire.Json)); }
    }
    static DynamicBlockPropertyValue ObjectIdentity(ObjectId id, Database database, out bool complete)
    {
        complete = true;
        if (id.IsNull) return new("object_id", Wire.Element(new { is_null = true, database_scope = "none" }));
        try
        {
            bool valid = id.IsValid;
            return new("object_id", Wire.Element(new
            {
                is_null = false,
                is_valid = valid,
                handle = valid ? OptionalId(id) : null,
                raw_id_token = id.OldIdPtr.ToInt64().ToString("X16"),
                is_erased = valid ? (bool?)id.IsErased : null,
                is_effectively_erased = valid ? (bool?)id.IsEffectivelyErased : null,
                database_scope = id.Database == null ? "unavailable" : id.Database.UnmanagedObject == database.UnmanagedObject ? "current_document" : "foreign_database",
                identity_scope = "current_native_ObjectId; no_foreign_EntityRef_or_persistent_alias"
            }));
        }
        catch (Teigha.Runtime.Exception error)
        { complete = false; return new("object_id", Wire.Element(new { is_null = false, observation_error = error.ErrorStatus.ToString(), database_scope = "unavailable" })); }
    }
    static bool WritableType(string type) => type is "double" or "int16" or "int32" or "string" or "point2d" or "point3d";
    static object NativeInput(DynamicBlockPropertyValue value) => value.type switch
    {
        "double" => value.value.GetDouble(),
        "int16" => value.value.GetInt16(),
        "int32" => value.value.GetInt32(),
        "string" => value.value.GetString()!,
        "point2d" => new Point2d(value.value[0].GetDouble(), value.value[1].GetDouble()),
        "point3d" => new Point3d(value.value[0].GetDouble(), value.value[1].GetDouble(), value.value[2].GetDouble()),
        _ => throw new DomainException("UNSUPPORTED_DYNAMIC_BLOCK_PROPERTY", "This native value type has no admitted setter.")
    };
    static object[] Buffer(DBObject item, Action cancel)
    {
        using var data = item.XData;
        return data == null ? [] : NativeOwnedMetadata.Buffer(data, cancel, new ModelInventoryBudget(cancel).Add,
            "dynamic_native[" + Id(item.ObjectId) + "].xdata");
    }
    static string[] Reactors(DBObject item, Action cancel)
    {
        using var ids = item.GetPersistentReactorIds(); var result = new List<string>();
        foreach (ObjectId id in ids) { cancel(); Need(Live(id), "A persistent native reference is unavailable."); result.Add(Id(id)); }
        return result.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }
    static void ProtectObject(DBObject item, Transaction tr, Action cancel)
    {
        cancel();
        if (item.HasFields || CadDrawingGroups431.HasNonOrganizationalReactors(item, tr, cancel))
            throw new DomainException("UNSUPPORTED_DYNAMIC_BLOCK_DEPENDENCY",
                "This actual native object has Fields or an unclassified persistent dependency. Its owning native lifecycle must be established before dynamic property mutation.",
                false, new
                {
                    guarded_handle = Id(item.ObjectId),
                    owner_handle = OptionalId(item.OwnerId),
                    native_type = item.GetType().FullName,
                    item.HasFields,
                    reactor_handles = Reactors(item, cancel)
                });
    }
    static void ProtectDefinitions(State state, Database db, Transaction tr, Action cancel)
    {
        var pending = new Queue<ObjectId>(); pending.Enqueue(state.Definition); pending.Enqueue(state.Representation);
        var visited = new HashSet<ObjectId>(); var budget = new ModelInventoryBudget(cancel);
        while (pending.Count > 0)
        {
            cancel(); var id = pending.Dequeue(); if (!visited.Add(id)) continue;
            var block = Definition(id, db, tr, false); ProtectObject(block, tr, cancel); budget.Add(Id(id));
            foreach (ObjectId member in block)
            {
                cancel(); if (!Live(member)) continue; var item = tr.GetObject(member, OpenMode.ForRead);
                Need(item is Entity && item.ObjectId == member && item.OwnerId == id && item.Database?.UnmanagedObject == db.UnmanagedObject,
                    "A native dynamic definition or representation member has inconsistent actual ownership.");
                ProtectObject(item, tr, cancel); budget.Add(Id(member));
                if (item is not BlockReference nested) continue;
                pending.Enqueue(nested.BlockTableRecord);
                if (nested.IsDynamicBlock) pending.Enqueue(nested.DynamicBlockTableRecord);
                foreach (ObjectId attributeId in nested.AttributeCollection)
                {
                    cancel(); Need(Live(attributeId), "A nested native occurrence owns an unavailable attribute.");
                    var attribute = tr.GetObject(attributeId, OpenMode.ForRead);
                    Need(attribute is AttributeReference && attribute.OwnerId == member && attribute.Database?.UnmanagedObject == db.UnmanagedObject,
                        "A nested native attribute has inconsistent actual ownership.");
                    ProtectObject(attribute, tr, cancel); budget.Add(Id(attributeId));
                }
            }
        }
    }
    static BlockTableRecord Definition(ObjectId id, Database db, Transaction tr, bool canonical)
    {
        Need(Live(id) && tr.GetObject(id, OpenMode.ForRead) is BlockTableRecord, "A native dynamic definition or representation is unavailable.");
        var block = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
        Need(block.GetType() == typeof(BlockTableRecord) && block.ObjectId == id && block.OwnerId == db.BlockTableId &&
            block.Database?.UnmanagedObject == db.UnmanagedObject && !block.IsLayout && !block.IsDependent &&
            !block.IsFromExternalReference && !block.IsFromOverlayReference,
            "The native definition or representation is foreign, external, custom or layout-owned.");
        if (canonical) Need(block.IsDynamicBlock && !block.IsAnonymous, "The selected occurrence has no ordinary native dynamic definition.");
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        Need(table.Has(block.Name) && table[block.Name] == id, "The native definition name index does not match its actual identity.");
        return block;
    }
    static JsonElement DefinitionSnapshot(BlockTableRecord block, Transaction tr, Action cancel)
    {
        var members = new List<object>(); var budget = new ModelInventoryBudget(cancel);
        foreach (ObjectId id in block)
        {
            cancel(); if (!Live(id)) continue; var item = tr.GetObject(id, OpenMode.ForRead);
            Need(item is Entity && item.OwnerId == block.ObjectId && item.Database?.UnmanagedObject == block.Database?.UnmanagedObject,
                "The native dynamic definition contains an inconsistent direct member.");
            var entity = (Entity)item;
            var summary = EntityAdapter.Summary(entity, ObjectId.Null, Zero, Zero);
            var row = new
            {
                handle = Id(id),
                owner = Id(item.OwnerId),
                native_type = item.GetType().FullName,
                extension = OptionalId(item.ExtensionDictionary),
                xdata = Buffer(item, cancel),
                reactors = Reactors(item, cancel),
                layer = Id(entity.LayerId),
                linetype = Id(entity.LinetypeId),
                display = CadBlockMover.PreservedProperties(entity),
                geometry = EntityAdapter.Detail(entity, summary, CancellationToken.None, includeNativeCurve: true).geometry
            };
            budget.Add(row); members.Add(row);
        }
        return Wire.Element(new
        {
            handle = Id(block.ObjectId),
            owner = Id(block.OwnerId),
            block.Name,
            origin = Point(block.Origin),
            units = (int)block.Units,
            scaling = (int)block.BlockScaling,
            block.Explodable,
            block.IsDynamicBlock,
            block.IsAnonymous,
            annotative = block.Annotative.ToString(),
            extension = OptionalId(block.ExtensionDictionary),
            xdata = Buffer(block, cancel),
            reactors = Reactors(block, cancel),
            members
        });
    }
    static JsonElement AttributeSnapshot(AttributeReference attribute, Transaction tr, Action cancel)
    {
        Need(attribute.GetType() == typeof(AttributeReference), "An owned dynamic attribute has a custom native type.");
        return Wire.Element(new
        {
            handle = Id(attribute.ObjectId),
            owner = Id(attribute.OwnerId),
            attribute.Tag,
            attribute.TextString,
            attribute.IsMTextAttribute,
            attribute.IsConstant,
            attribute.LockPositionInBlock,
            style = Id(attribute.TextStyleId),
            attribute.Height,
            attribute.WidthFactor,
            attribute.Oblique,
            attribute.Thickness,
            horizontal = (int)attribute.HorizontalMode,
            vertical = (int)attribute.VerticalMode,
            layer = Id(attribute.LayerId),
            linetype = Id(attribute.LinetypeId),
            display = CadBlockMover.PreservedProperties(attribute),
            position = Point(attribute.Position),
            alignment = Point(attribute.AlignmentPoint),
            normal = Vector(attribute.Normal),
            attribute.Rotation,
            attribute.Invisible,
            attribute.IsMirroredInX,
            attribute.IsMirroredInY,
            multiline = NativeBlockMultiline442.Capture(attribute, cancel),
            xdata = Buffer(attribute, cancel),
            metadata = NativeOwnedMetadata.Capture(attribute, tr, cancel, observationPath: "dynamic_native[" + Id(attribute.ObjectId) + "]")
        });
    }
    static State Capture(BlockReference reference, Transaction tr, string bridge, string document, Action cancel)
    {
        cancel(); var db = reference.Database;
        Need(db != null && reference.GetType() == typeof(BlockReference) && Live(reference.ObjectId) && reference.IsDynamicBlock,
            "Select an actual existing exact native dynamic BlockReference, not an array or definition.");
        _ = CadBlockSpace.RequireTopLevel(reference, db, tr);
        var definition = Definition(reference.DynamicBlockTableRecord, db, tr, true);
        var representation = Definition(reference.BlockTableRecord, db, tr, false);
        Need(representation.ObjectId == definition.ObjectId || representation.IsAnonymous,
            "The occurrence presentation is not its actual dynamic definition or native anonymous representation.");
        ObjectId anonymous = reference.AnonymousBlockTableRecord;
        if (!anonymous.IsNull) Need(Definition(anonymous, db, tr, false).IsAnonymous && anonymous == representation.ObjectId,
            "Native anonymous representation identity is inconsistent.");
        var attributes = new Dictionary<ObjectId, JsonElement>(); var snapshotBudget = new ModelInventoryBudget(cancel);
        foreach (ObjectId id in reference.AttributeCollection)
        {
            cancel(); Need(Live(id) && tr.GetObject(id, OpenMode.ForRead) is AttributeReference, "An actual owned dynamic attribute is unavailable.");
            var attribute = (AttributeReference)tr.GetObject(id, OpenMode.ForRead);
            Need(attribute.OwnerId == reference.ObjectId && attribute.Database?.UnmanagedObject == db.UnmanagedObject && !attributes.ContainsKey(id),
                "An owned dynamic attribute has foreign or repeated identity.");
            var attributeState = AttributeSnapshot(attribute, tr, cancel); snapshotBudget.Add(attributeState); attributes.Add(id, attributeState);
        }
        var root = Wire.Element(new
        {
            handle = Id(reference.ObjectId),
            owner = Id(reference.OwnerId),
            transform = Matrix(reference.BlockTransform),
            position = Point(reference.Position),
            normal = Vector(reference.Normal),
            reference.Rotation,
            scale = new[] { Finite(reference.ScaleFactors.X), Finite(reference.ScaleFactors.Y), Finite(reference.ScaleFactors.Z) },
            layer = Id(reference.LayerId),
            linetype = Id(reference.LinetypeId),
            display = CadBlockMover.PreservedProperties(reference),
            reference.HasFields,
            annotative = reference.Annotative.ToString(),
            extension = OptionalId(reference.ExtensionDictionary),
            xdata = Buffer(reference, cancel),
            reactors = Reactors(reference, cancel),
            attribute_ids = attributes.Keys.Select(Id).ToArray()
        });
        var definitionState = DefinitionSnapshot(definition, tr, cancel); snapshotBudget.Add(root); snapshotBudget.Add(definitionState);
        var rows = new List<DynamicBlockPropertyInfo>();
        bool complete = true; var budget = new ModelInventoryBudget(cancel);
        using (var collection = reference.DynamicBlockReferencePropertyCollection)
        {
            for (int index = 0; index < collection.Count; index++)
            {
                cancel(); var property = collection[index];
                var blockId = property.BlockId;
                var nativeBlock = ObjectIdentity(blockId, db, out bool blockObserved);
                bool belongs = blockId == reference.ObjectId;
                object? native = property.Value; var value = NativeValue(native, db, out bool observed); observed &= blockObserved && belongs;
                var allowed = new List<DynamicBlockPropertyValue>();
                foreach (object? candidate in property.GetAllowedValues())
                { cancel(); allowed.Add(NativeValue(candidate, db, out bool readable)); observed &= readable; }
                complete &= observed;
                string? unavailable = !belongs ? "The native property BlockId does not match this occurrence; no alternate property identity is inferred."
                    : !observed ? "A native value type or identity is not completely observed; this inventory cannot authorize a write."
                    : property.ReadOnly ? "The native property is read-only."
                    : !WritableType(value.type) ? "This observed native value type has no admitted setter." : null;
                var row = new DynamicBlockPropertyInfo(index, property.PropertyName, property.Description, property.PropertyTypeCode, nativeBlock,
                    (int)property.UnitsType, property.UnitsType.ToString(), native?.GetType().FullName ?? "null", value, allowed.ToArray(),
                    property.ReadOnly, property.Show, property.VisibleInCurrentVisibilityState, unavailable == null, unavailable);
                budget.Add(row); snapshotBudget.Add(row); rows.Add(row);
            }
        }
        EntityRef Ref(ObjectId id) => new(bridge, document, Id(id));
        string token = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            bridge,
            document,
            root,
            definition = definitionState,
            representation = Id(representation.ObjectId),
            anonymous = OptionalId(anonymous),
            attributes = attributes.Values.ToArray(),
            properties = rows,
            complete
        }, Wire.Json)));
        var inventory = new DynamicBlockPropertiesInventory(Ref(reference.ObjectId), Ref(definition.ObjectId), Ref(representation.ObjectId),
            anonymous.IsNull ? null : Ref(anonymous), token, "current_complete_snapshot_ordinal", "native_parameter_units_and_frame; no_conversion",
            rows.ToArray(), complete, false);
        new ModelInventoryBudget(cancel).Add(inventory);
        return new(reference.ObjectId, definition.ObjectId, representation.ObjectId, anonymous, root, definitionState, attributes, inventory);
    }
    internal static DynamicBlockPropertiesInventory Read(BlockReference reference, Transaction tr, string bridge, string document, Action cancel) =>
        Capture(reference, tr, bridge, document, cancel).Inventory;

    static Binding Resolve(ModifyArguments args, DocumentState state, Transaction tr, Action cancel)
    {
        DynamicBlockPropertiesValidation.Batch(args.Operations);
        Need(args.Operations is [SetDynamicBlockPropertySpec], "Set one observed native dynamic property in a separate request.", "INVALID_ARGUMENT");
        var edit = (SetDynamicBlockPropertySpec)args.Operations[0]; var db = state.Document.Database;
        var id = CadStructureWriter.Find(db, edit.Handle);
        Need(Live(id) && tr.GetObject(id, OpenMode.ForRead) is BlockReference, "The selected dynamic occurrence is unavailable.", "OBJECT_NOT_FOUND");
        var reference = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
        var owner = CadBlockSpace.RequireWriteSpace(db, tr, args.TargetSpace, cancel);
        Need(reference.OwnerId == owner, "The dynamic occurrence must belong directly to the requested active native Model or paper space.", "OUT_OF_SCOPE");
        Need(reference.Annotative != AnnotativeStates.True, "Annotative occurrences require their owning context lifecycle.");
        ProtectObject(reference, tr, cancel);
        var layer = (LayerTableRecord)tr.GetObject(reference.LayerId, OpenMode.ForRead);
        new LayerWriteState(layer.Name, layer.ObjectId == db.Clayer, layer.IsDependent, layer.IsOff, layer.IsFrozen, layer.IsLocked).RequireAttributeWrite();
        foreach (ObjectId attributeId in reference.AttributeCollection)
        {
            cancel(); var attribute = tr.GetObject(attributeId, OpenMode.ForRead);
            ProtectObject(attribute, tr, cancel);
            Need(attribute is AttributeReference, "A dynamic occurrence owns a non-attribute member.");
            var attributeLayer = (LayerTableRecord)tr.GetObject(((AttributeReference)attribute).LayerId, OpenMode.ForRead);
            new LayerWriteState(attributeLayer.Name, attributeLayer.ObjectId == db.Clayer, attributeLayer.IsDependent,
                attributeLayer.IsOff, attributeLayer.IsFrozen, attributeLayer.IsLocked).RequireAttributeWrite();
        }
        var before = Capture(reference, tr, args.BridgeId, args.DocumentId, cancel);
        ProtectDefinitions(before, db, tr, cancel);
        Need(before.Inventory.complete, "The complete native property state is unavailable; no write is admitted.");
        Need(before.Inventory.properties_token == edit.ExpectedPropertiesToken, "The native dynamic definition, representation, placement, attributes or complete property inventory changed; refresh inspect_objects.", "STALE_DYNAMIC_BLOCK_TOKEN");
        Need(edit.PropertyIndex < before.Inventory.properties.Length, "The current property index is outside the observed collection.", "PRECONDITION_FAILED");
        var selected = before.Inventory.properties[edit.PropertyIndex];
        Need(selected.name == edit.PropertyName, "The exact property name does not match that current snapshot index.", "PRECONDITION_FAILED");
        Need(selected.editable && !selected.read_only, selected.unavailable_reason ?? "The native property is not editable.");
        Need(selected.value.type == edit.Value.type, "Use the exact observed native value type, without numeric coercion.", "INVALID_ARGUMENT");
        Need(selected.allowed_values.Length == 0 || selected.allowed_values.Any(value => DynamicBlockPropertiesValidation.SameValue(value, edit.Value)),
            "The requested value is not one of the actual native enumerated allowed values.", "INVALID_ARGUMENT");
        SerializedCapacity357.Check(DynamicBlockPropertiesValidation.ProjectedResult(args, edit, before.Inventory,
            before.Attributes.Keys.Select(id => new EntityRef(args.BridgeId, args.DocumentId, Id(id))).ToArray()), Wire.PayloadBudget - 8192, cancel);
        return new(edit, before);
    }
    internal static void Preflight(ModifyArguments args, DocumentState state, Transaction tr, Action cancel) => Resolve(args, state, tr, cancel);
    static DynamicBlockPropertyChange Verify(Binding binding, State after, DynamicBlockPropertyValue nativeValue, string bridge, string document)
    {
        var before = binding.Before; var edit = binding.Edit;
        Need(after.Id == before.Id && after.Definition == before.Definition &&
            Same(DynamicBlockPropertiesValidation.RetainedRootState(before.Root), DynamicBlockPropertiesValidation.RetainedRootState(after.Root)) &&
            Same(before.DefinitionState, after.DefinitionState),
            "The native evaluator changed the retained root identity/placement/display, actual canonical definition or root user state.", Mismatch);
        Need(after.Inventory.complete, "The native property collection became incompletely observed.", Mismatch);
        var changes = new List<DynamicBlockObservedPropertyChange>();
        for (int index = 0; index < Math.Max(before.Inventory.properties.Length, after.Inventory.properties.Length); index++)
        {
            var prior = index < before.Inventory.properties.Length ? before.Inventory.properties[index] : null;
            var actual = index < after.Inventory.properties.Length ? after.Inventory.properties[index] : null;
            bool requested = index == edit.PropertyIndex;
            // This is an explicit array-slot comparison, not correspondence of
            // native parameter identities after lookup/visibility regeneration.
            if (requested || prior == null || actual == null || !Same(Wire.Element(prior), Wire.Element(actual)))
                changes.Add(new(index, prior?.name ?? actual!.name, prior, actual, requested));
        }
        Need(before.Attributes.Count == after.Attributes.Count && before.Attributes.Keys.ToHashSet().SetEquals(after.Attributes.Keys),
            "The native evaluator replaced an owned attribute; no identity correspondence is fabricated.", Mismatch);
        var attributes = new List<DynamicBlockAttributeChange>();
        foreach (var prior in before.Attributes)
        {
            var actual = after.Attributes[prior.Key];
            Need(Same(DynamicBlockPropertiesValidation.RetainedAttributeState(prior.Value), DynamicBlockPropertiesValidation.RetainedAttributeState(actual)),
                "Native dynamic evaluation changed an owned attribute's actual identity, owner, literal, user format, style or native user bindings.", Mismatch);
            if (!Same(prior.Value, actual)) attributes.Add(new(new(bridge, document, Id(prior.Key)), prior.Value, actual));
        }
        EntityRef Ref(ObjectId id) => new(bridge, document, Id(id));
        return new(0, Ref(after.Id), Ref(after.Definition), Ref(before.Representation), Ref(after.Representation), edit.PropertyIndex,
            edit.PropertyName, edit.Value, nativeValue, DynamicBlockPropertiesValidation.ComparisonScope,
            changes.ToArray(), attributes.ToArray(), before.Attributes.Keys.Select(Ref).ToArray(), after.Attributes.Keys.Select(Ref).ToArray(), after.Inventory);
    }
    internal static Result Execute(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        Transaction? tr = null; bool started = false, commitAttempted = false; string stage = "resolve";
        try
        {
            var db = state.Document.Database; tr = db.TransactionManager.StartTransaction(); var binding = Resolve(args, state, tr, job.CheckCancellation);
            var reference = (BlockReference)tr.GetObject(binding.Before.Id, OpenMode.ForWrite);
            // Acquire one fresh wrapper after complete current-token admission.
            // Use it for one setter and its immediate getter witness only;
            // never retarget another operation through a regenerated collection.
            stage = "native_property_set"; DynamicBlockPropertyValue nativeValue;
            using (var properties = reference.DynamicBlockReferencePropertyCollection)
            {
                var selected = properties[binding.Edit.PropertyIndex];
                Need(selected.BlockId == reference.ObjectId && selected.PropertyName == binding.Edit.PropertyName && !selected.ReadOnly,
                    "The freshly acquired native property wrapper no longer matches the admitted snapshot.", "PRECONDITION_FAILED");
                job.CheckCancellation(); started = true; log.Write(job.Operation, job.OriginalRequestId, "dynamic_block_property_started", operationId: job.OperationId);
                selected.Value = NativeInput(binding.Edit.Value);
                nativeValue = NativeValue(selected.Value, db, out bool witnessed);
                Need(witnessed && DynamicBlockPropertiesValidation.SameValue(binding.Edit.Value, nativeValue),
                    "The actual selected native property rejected or changed the requested value.", Mismatch);
            }
            stage = "native_readback"; var after = Capture(reference, tr, args.BridgeId, args.DocumentId, job.CheckCancellation);
            ProtectDefinitions(after, db, tr, job.CheckCancellation);
            var change = Verify(binding, after, nativeValue, args.BridgeId, args.DocumentId);
            var data = new ModifiedEntities(args.OperationId, "committed", false, [new(args.BridgeId, args.DocumentId, Id(after.Id))], [], [], [], "not_guaranteed")
            { dynamic_block_changes = [change] };
            SerializedCapacity357.Check(data, Wire.PayloadBudget - 8192, job.CheckCancellation);
            job.CheckCancellation(); stage = "commit"; job.BeginCommit(); commitAttempted = true; tr.Commit(); tr.Dispose(); tr = null;
            stage = "postcommit_readback";
            using (var read = db.TransactionManager.StartTransaction())
            {
                var final = Capture((BlockReference)read.GetObject(after.Id, OpenMode.ForRead), read, args.BridgeId, args.DocumentId, job.CheckCancellation);
                ProtectDefinitions(final, db, read, job.CheckCancellation);
                var actual = Verify(binding, final, nativeValue, args.BridgeId, args.DocumentId);
                Need(Same(Wire.Element(change), Wire.Element(actual)), "The committed native evaluation differs from the verified property receipt.", Mismatch);
            }
            var result = Result.Ok(job.OriginalRequestId, data, context, Warning, WriteRecovery.Warning); job.RecordCommit(result); return result;
        }
        catch (System.Exception error)
        {
            if (job.CommittedResult is { } committed) return committed;
            System.Exception? cleanup = null; try { tr?.Dispose(); } catch (System.Exception caught) { cleanup = caught; }
            string cause = error is DomainException domain ? domain.Error.code : error is OperationCanceledException ? "CANCELLED" : "DYNAMIC_BLOCK_PROPERTY_FAILED";
            return job.Error(started ? "WRITE_OUTCOME_UNKNOWN" : cause,
                started ? "Native dynamic evaluation started, but its complete outcome is unverified. Inspect the actual occurrence, complete native properties and representation before dependent writes."
                    : error.Message, started ? "unknown" : "not_started", context,
                new
                {
                    cause,
                    stage,
                    commit_attempted = commitAttempted,
                    message = error.Message,
                    native_details = (error as DomainException)?.Error.details,
                    cleanup_error = cleanup?.GetType().Name
                })
                with
            { warnings = [Warning, WriteRecovery.Warning] };
        }
    }
}
