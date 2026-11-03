using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// One OdDbMInsertBlock, never a set of independent copies or an AssocArray action network.
internal static class CadBlockArray384
{
    internal sealed record Snapshot(JsonElement Root, JsonElement Definition, NativeBlockArrayParameters384 Parameters);
    internal sealed record Resolved(StructureEdit Edit, ObjectId Target, CadBlockWriter.Resolved Definition,
        Snapshot? Before, JsonElement Source);
    private static void Need([DoesNotReturnIf(false)] bool value, string message, string code = "UNSUPPORTED_BLOCK_ARRAY")
    { if (!value) throw new DomainException(code, message); }
    private static string? Id(ObjectId id) => id.IsNull ? null : Validation.NormalizeHandle(id.Handle.ToString());
    private static bool Same(JsonElement a, JsonElement b) => a.GetRawText() == b.GetRawText();
    private static double[] Point(Point3d p) => new[] { p.X, p.Y, p.Z };
    private static double[] Vector(Vector3d p) => new[] { p.X, p.Y, p.Z };
    internal static NativeBlockArrayParameters384 Parameters(MInsertBlock array) => new(
        unchecked((ushort)array.Rows), unchecked((ushort)array.Columns), array.RowSpacing, array.ColumnSpacing);
    private static void Set(MInsertBlock array, NativeBlockArrayParameters384 parameters)
    {
        NativeBlockArrayValidation384.Validate(parameters);
        array.Rows = unchecked((short)parameters.Rows); array.Columns = unchecked((short)parameters.Columns);
        array.RowSpacing = parameters.RowSpacing; array.ColumnSpacing = parameters.ColumnSpacing;
    }
    private static void CheckParameters(MInsertBlock array, NativeBlockArrayParameters384 expected)
    {
        var actual = Parameters(array);
        Need(actual.Rows == expected.Rows && actual.Columns == expected.Columns &&
            BlockMoveGeometry.Close(actual.RowSpacing, expected.RowSpacing) && BlockMoveGeometry.Close(actual.ColumnSpacing, expected.ColumnSpacing),
            "Native MInsert count/spacing setters did not retain the requested values.", "NATIVE_ARRAY_MISMATCH");
    }
    private static JsonElement Metadata(DBObject root, Transaction tr, Action cancel)
    {
        cancel(); using var xdata = root.XData; using var reactors = root.GetPersistentReactorIds();
        var budget = new SerializedCapacity357(Wire.PayloadBudget - 8192, cancel); var links = new List<string?>();
        foreach (ObjectId id in reactors) { cancel(); var value = Id(id); budget.Add(value!); links.Add(value); }
        var result = Wire.Element(new { handle = Id(root.ObjectId), owner = Id(root.OwnerId), type = root.GetType().FullName,
            root.HasFields, extension = Id(root.ExtensionDictionary), xdata = xdata == null ? null : NativeOwnedMetadata.Buffer(xdata, cancel, budget.Add),
            reactors = links.ToArray(), owned = NativeOwnedMetadata.Capture(root, tr, cancel) });
        SerializedCapacity357.Check(result, Wire.PayloadBudget - 8192, cancel); return result;
    }
    private static JsonElement Source(CadBlockWriter.Resolved definition, Transaction tr, Action cancel)
    {
        definition.Graph?.Verify(tr,cancel);
        var budget = new SerializedCapacity357(Wire.PayloadBudget - 8192, cancel);
        var block = (BlockTableRecord)tr.GetObject(definition.Definition, OpenMode.ForRead);
        var members = new List<object>();
        foreach (ObjectId id in block)
        {
            cancel(); if (id.IsErased || id.IsEffectivelyErased) continue;
            Need(tr.GetObject(id, OpenMode.ForRead) is Entity, "Shared array definition has an unavailable entity.");
            var entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
            var item = new { metadata = Metadata(entity, tr, cancel), stored = CadBlockMover.PreservedProperties(entity),
                detail = EntityAdapter.Detail(entity, EntityAdapter.Summary(entity, ObjectId.Null, "00000000-0000-0000-0000-000000000000",
                    "00000000-0000-0000-0000-000000000000"), CancellationToken.None) };
            budget.Add(item); members.Add(item);
        }
        var result = Wire.Element(new { metadata = Metadata(block, tr, cancel), block.Name, origin = Point(block.Origin),
            units = (int)block.Units, scaling = (int)block.BlockScaling, block.Explodable, members = members.ToArray(),
            retained_graph=definition.Graph?.States.Select(s=>new{handle=Id(s.Id),state=s.Value}).ToArray() });
        SerializedCapacity357.Check(result, Wire.PayloadBudget - 8192, cancel); return result;
    }
    private static void RootScope(MInsertBlock array, Database db, Transaction tr, ObjectId space, Action cancel)
    {
        cancel(); Need(array.GetType() == typeof(MInsertBlock) && array.Database?.UnmanagedObject == db.UnmanagedObject &&
            !array.ObjectId.IsNull && array.ObjectId.IsValid && !array.ObjectId.IsErased && !array.ObjectId.IsEffectivelyErased && array.OwnerId == space,
            "Select an exact live local top-level native MInsertBlock in the requested space.");
        _ = CadBlockSpace.RequireSpace(db, tr, space);
        Need((int)db.Insunits == 4, "Native array size editing requires a Millimeters drawing; no implicit scaling is performed.", "UNSUPPORTED_UNITS");
        Need(!array.HasFields&&!CadDrawingGroups431.HasNonOrganizationalReactors(array,tr,cancel)&&array.Annotative != AnnotativeStates.True && !array.IsDynamicBlock,
            "Associated, Field-bearing, annotative or dynamic array roots require their owning route.");
        Need(!array.AttributeCollection.Cast<ObjectId>().Any(), "This native block-array adapter retains attribute-free occurrences; per-instance attribute semantics are outside this scope.");
        CadBlockMover.CheckRoot(array);
        NativeBlockArrayValidation384.Validate(Parameters(array));
    }
    private static JsonElement Root(MInsertBlock array, Transaction tr, Action cancel) => Wire.Element(new {
        metadata = Metadata(array, tr, cancel), definition = Id(array.BlockTableRecord), position = Point(array.Position),
        array.Rotation, scale = new[] { array.ScaleFactors.X, array.ScaleFactors.Y, array.ScaleFactors.Z }, normal = Vector(array.Normal),
        stored = CadBlockMover.PreservedProperties(array) });
    private static string Token(Snapshot value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json)));
    private static CadBlockWriter.Resolved Definition(MInsertBlock array, Database db, Transaction tr, ObjectId space,
        Func<ObjectId, LayerWriteState> layer, Action cancel)
    {
        var result=CadBlockMover.Resolve(array,db,tr,space,cancel);
        Need(result.Attributes.Length==0,"Native arrays retain their existing attribute-free shared definitions; per-occurrence attribute copies are not fabricated.");
        return result.Definition;
    }
    internal static Resolved Resolve(StructureEdit edit, Database db, Transaction tr, ObjectId space,
        Func<ObjectId, LayerWriteState> layer, Action cancel)
    {
        if (edit is InsertBlockArray384 insert)
        {
            NativeBlockArrayValidation384.Validate(insert.Parameters);
            var definition = CadBlockWriter.Resolve(new InsertBlockEdit(insert.DefinitionHandle, insert.Position, insert.Layer, insert.Rotation,
                insert.Scale, new(StringComparer.Ordinal)), db, tr, space, layer, cancel);
            Need(definition.AttributeIds.Length == 0, "Native array insertion requires an attribute-free supported static definition.");
            return new(edit, ObjectId.Null, definition, null, Source(definition, tr, cancel));
        }
        var set = (SetBlockArray384)edit; var id = CadStructureWriter.Find(db, set.Handle);
        Need(tr.GetObject(id, OpenMode.ForRead) is MInsertBlock, "The selected handle is not a native MInsertBlock.");
        var array = (MInsertBlock)tr.GetObject(id, OpenMode.ForRead); RootScope(array, db, tr, space, cancel);
        var existing = Definition(array, db, tr, space, layer, cancel); var source = Source(existing, tr, cancel);
        var before = new Snapshot(Root(array, tr, cancel), source, Parameters(array));
        SerializedCapacity357.Check(before, Wire.PayloadBudget - 8192, cancel);
        Need(Token(before) == set.ArrayToken, "Array or shared definition changed since the read; refresh inspect_objects.", "STALE_ARRAY_TOKEN");
        NativeBlockArrayValidation384.Validate(set.Properties.Apply(before.Parameters));
        return new(edit, id, existing, before, source);
    }
    internal static MInsertBlock Preview(Resolved selected, Transaction tr, List<DBObject> owned, Action cancel)
    {
        cancel(); var position = selected.Definition.Placement!;
        var parameters = selected.Edit is InsertBlockArray384 insert ? insert.Parameters : ((SetBlockArray384)selected.Edit).Properties.Apply(selected.Before!.Parameters);
        var array = new MInsertBlock(new(position.Position.X, position.Position.Y, 0), selected.Definition.Definition,
            unchecked((short)parameters.Columns), unchecked((short)parameters.Rows), parameters.ColumnSpacing, parameters.RowSpacing); owned.Add(array);
        Need(array.ObjectId.IsNull, "Native array preview must be detached.", "API_EXCEPTION");
        if (selected.Edit is SetBlockArray384)
        {
            var original = (MInsertBlock)tr.GetObject(selected.Target, OpenMode.ForRead);
            array.SetPropertiesFrom(original); array.Normal = original.Normal; array.Position = original.Position;
            array.Rotation = original.Rotation; array.ScaleFactors = original.ScaleFactors;
            Set(array, parameters);
        }
        else
        {
            array.Normal = Vector3d.ZAxis; array.Rotation = position.Rotation; array.ScaleFactors = new Scale3d(position.Scale);
            array.LayerId = selected.Definition.InsertionLayer; array.ColorIndex = 256; array.LinetypeId = selected.Definition.ByLayerLinetype; array.LineWeight = (LineWeight)(-1);
        }
        CheckParameters(array, parameters); cancel();
        var transform = array.BlockTransform;
        var expectedTransform = selected.Edit is SetBlockArray384
            ? ((MInsertBlock)tr.GetObject(selected.Target,OpenMode.ForRead)).BlockTransform
            : new Matrix3d(position.Matrix());
        for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
            Need(BlockMoveGeometry.Close(transform[row,column], expectedTransform[row,column]),
                "Native array placement differs from the retained or requested block placement.", "NATIVE_ARRAY_MISMATCH");
        return array;
    }
    internal static StructureChange Project(StructureWriteArguments args, int index, Resolved selected) => new(index,
        selected.Edit.Operation, new(args.BridgeId, args.DocumentId, "FFFFFFFFFFFFFFFF"), "block_array", selected.Definition.Name, selected.Edit is InsertBlockArray384);
    internal static StructureChange Apply(StructureWriteArguments args, int index, Resolved selected, MInsertBlock preview,
        Database db, Transaction tr, WriteJob job)
    {
        job.CheckCancellation();
        Need(CadBlockSpace.RequireWriteSpace(db, tr, args.EffectiveTargetSpace, job.CheckCancellation) == selected.Definition.Model,
            "Native array target space changed before apply.", "OUT_OF_SCOPE");
        MInsertBlock array; ObjectId id;
        if (selected.Edit is InsertBlockArray384 insert)
        {
            array = preview; id = ((BlockTableRecord)tr.GetObject(selected.Definition.Model, OpenMode.ForWrite)).AppendEntity(array);
            tr.AddNewlyCreatedDBObject(array, true);
            Need(array.ObjectId == id && array.OwnerId == selected.Definition.Model && array.BlockTableRecord == selected.Definition.Definition,
                "Native array insertion changed its identity, owner or shared definition.", "NATIVE_ARRAY_MISMATCH");
            CheckParameters(array, insert.Parameters);
        }
        else
        {
            var set = (SetBlockArray384)selected.Edit; id = selected.Target;
            array = (MInsertBlock)tr.GetObject(id, OpenMode.ForWrite);
            RootScope(array, db, tr, selected.Definition.Model, job.CheckCancellation);
            Need(Token(new(Root(array, tr, job.CheckCancellation), Source(selected.Definition, tr, job.CheckCancellation), Parameters(array))) == set.ArrayToken,
                "Native array changed before its requested setters.", "STALE_ARRAY_TOKEN");
            var expected = set.Properties.Apply(selected.Before!.Parameters); Set(array, expected); CheckParameters(array, expected);
            Need(Same(selected.Before.Root, Root(array, tr, job.CheckCancellation)), "Native array edit changed unrequested placement, identity, display or owned data.", "NATIVE_ARRAY_MISMATCH");
        }
        Need(Same(selected.Source, Source(selected.Definition, tr, job.CheckCancellation)), "Native array write changed the retained shared definition or source members.", "BLOCK_DEFINITION_CHANGED");
        return new(index, selected.Edit.Operation, new(args.BridgeId, args.DocumentId, Id(id)!), "block_array", selected.Definition.Name, selected.Edit is InsertBlockArray384);
    }
    internal static object Read(MInsertBlock array, EntityRef context, Transaction tr, Action cancel)
    {
        cancel(); var parameters = Parameters(array); string? token = null, reason = null;
        try
        {
            var db = array.Database; Need(db != null, "Native array has no current database.");
            RootScope(array, db, tr, array.OwnerId, cancel);
            LayerWriteState Layer(ObjectId id)
            { var item = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead); return new(item.Name, id == db.Clayer, item.IsDependent, item.IsOff, item.IsFrozen, item.IsLocked); }
            var definition = Definition(array, db, tr, array.OwnerId, Layer, cancel);
            var snapshot = new Snapshot(Root(array, tr, cancel), Source(definition, tr, cancel), parameters);
            SerializedCapacity357.Check(snapshot, Wire.PayloadBudget - 8192, cancel); token = Token(snapshot);
        }
        catch (DomainException error) { cancel(); reason = error.Error.code + ": " + error.Message; }
        catch (Teigha.Runtime.Exception error) { cancel(); reason = "NATIVE_ARRAY_EDITING_UNAVAILABLE: " + error.ErrorStatus; }
        EntityRef Identity(ObjectId id)
        { Need(!id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased, "Native array owner/definition identity is unavailable.", "OBJECT_READ_FAILED"); return new(context.bridge_instance_id, context.document_id, Id(id)!); }
        object? bounds = null; string? boundsReason = null;
        try
        {
            cancel(); var extents = CadMInsertBounds407.Read(array,cancel);
            Need(Point(extents.MinPoint).Concat(Point(extents.MaxPoint)).All(double.IsFinite), "Native array bounds are nonfinite.", "OBJECT_READ_FAILED");
            bounds = new { min = Point(extents.MinPoint), max = Point(extents.MaxPoint) };
        }
        catch (Teigha.Runtime.Exception error) { cancel(); boundsReason = "NATIVE_ARRAY_BOUNDS_UNAVAILABLE: " + error.ErrorStatus; }
        catch (DomainException error) { cancel(); boundsReason = error.Error.code + ": " + error.Message; }
        var result = new { inspection_scope = "native_minsert_parameters", native_type = "MInsertBlock", owner_ref = Identity(array.OwnerId),
            definition_ref = Identity(array.BlockTableRecord), position = Point(array.Position), rotation = array.Rotation,
            scale_factors = new[] { array.ScaleFactors.X, array.ScaleFactors.Y, array.ScaleFactors.Z }, normal = Vector(array.Normal),
            rows = parameters.Rows, columns = parameters.Columns, row_spacing = parameters.RowSpacing, column_spacing = parameters.ColumnSpacing,
            instance_count = (long)parameters.Rows * parameters.Columns, array_token = token, editing_unavailable_reason = reason,
            bounds, bounds_kind = bounds==null?null:CadMInsertBounds407.Kind, bounds_unavailable_reason = boundsReason };
        cancel(); return result;
    }
}
