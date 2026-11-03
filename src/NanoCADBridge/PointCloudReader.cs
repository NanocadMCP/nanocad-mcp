using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Cloud = HostMgd.PointClouds;

namespace NanoCADBridge;

internal static class PointCloudReader
{
    internal const string SourceUnitsNote = "The original import-source units are not established by this read. Coordinates and linear results use the current insertion and drawing units; no millimeter conversion is assumed.";
    internal const string FeatureScope = "Observed stored native features; geometry is in the native feature frame, without a WCS transformation or fitting claim.";

    // Keep the legacy internal caller while the public parser supplies the extended arguments.
    public static object Read(Arguments args, DocumentState state, Transaction tx, ObjectId model, string instance, CancellationToken cancellation) =>
        Read(new PointCloudInspectArguments(args, "metadata"), state, tx, model, instance, cancellation);

    public static object Read(PointCloudInspectArguments args, DocumentState state, Transaction tx, ObjectId model, string instance, CancellationToken cancellation)
    {
        var id = Resolve(args.Read, state, tx, model, cancellation);
        return ReadLoaded(args, id, state, tx, instance, cancellation);
    }

    internal static ObjectId Resolve(Arguments args, DocumentState state, Transaction tx, ObjectId model, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        PointCloudRuntime.RequireLoaded();
        long bits = unchecked((long)ulong.Parse(args.Handles![0], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        if (!state.Document.Database.TryGetObjectId(new Handle(bits), out var id) || id.IsNull || !id.IsValid)
            throw new DomainException("OBJECT_NOT_FOUND", "Point cloud insertion not found.");
        if (id.IsErased || id.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED", "Point cloud insertion was erased.");
        if (tx.GetObject(id, OpenMode.ForRead) is not Entity entity || entity.OwnerId != model)
            throw new DomainException("OUT_OF_SCOPE", "Cloud insertion must be top-level Model Space.");
        return id;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object ReadLoaded(PointCloudInspectArguments args, ObjectId id, DocumentState state, Transaction tx, string instance, CancellationToken cancellation)
    {
        using var source = Source.Open(id, state, tx, instance, cancellation);
        if (args.Mode == "features") return PointCloudAnalysisReader.ReadFeatures(args, source, cancellation);
        source.Verify(cancellation);
        return source.Metadata("metadata", null, null, null);
    }

    internal static string? Text(string? value) => value == null || value.Length <= 8192 ? value :
        throw new DomainException("ENTITY_TOO_LARGE", "Cloud metadata text exceeds the per-value transport budget.");
    internal static double Finite(double value) => double.IsFinite(value) ? value :
        throw new DomainException("INVALID_GEOMETRY", "A point-cloud value is not finite.");
    internal static double[] Point(Point3d point) => [Finite(point.X), Finite(point.Y), Finite(point.Z)];
    internal static double[] Vector(Vector3d vector) => [Finite(vector.X), Finite(vector.Y), Finite(vector.Z)];
    internal static string Decimal(ulong value) => value.ToString(CultureInfo.InvariantCulture);
    internal static string Digest(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json)));
    internal static double[] Matrix(Matrix3d value)
    {
        var array = value.ToArray();
        if (array.Length != 16 || array.Any(v => !double.IsFinite(v)))
            throw new DomainException("POINT_CLOUD_FRAME_UNAVAILABLE", "Native cloud transformation is not a finite 4x4 matrix.");
        return array;
    }

    internal static T NativeRead<T>(string stage, Func<T> read)
    {
        using var phase = PointCloudReadScope402.Stage(stage);
        try { return read(); }
        catch (DomainException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Teigha.Runtime.Exception exception)
        {
            throw new DomainException("POINT_CLOUD_NATIVE_READ_FAILED", "nanoCAD could not complete the point-cloud read at " + stage + ".", details: new
            { native_stage = stage, api_status = exception.ErrorStatus.ToString(), update_on_releasing = false,
                access_policy = "getters_only_no_source_update", cloud_setters_called = false });
        }
        catch (System.Exception exception)
        {
            throw new DomainException("POINT_CLOUD_NATIVE_READ_FAILED", "The point-cloud read could not complete at " + stage + ".", details: new
            { native_stage = stage, error_type = exception.GetType().Name, update_on_releasing = false,
                access_policy = "getters_only_no_source_update", cloud_setters_called = false });
        }
    }
    internal static void NativeRead(string stage, Action read) => NativeRead(stage, () => { read(); return true; });

    // Each request owns and releases these native wrappers on the existing host read thread.
    // No wrapper, entity or iterator is stored in a continuation cursor.
    internal sealed class Source : IDisposable
    {
        internal readonly Cloud.PointCloudInsertion Insertion;
        private readonly Cloud.PointCloudDefinition _definition;
        private readonly Entity _resident;
        internal readonly Cloud.PointCloudInfo? Info;
        internal readonly DocumentState State;
        internal readonly ObjectId Id;
        internal readonly ObjectId DefinitionId;
        internal readonly EntityRef SourceRef;
        internal readonly EntityRef DefinitionRef;
        internal readonly string Fingerprint;
        internal readonly bool Loaded;
        internal readonly string? Path;
        internal readonly string? Name;
        private readonly object? _bounds;
        private readonly string _scope;

        private Source(Cloud.PointCloudInsertion insertion, Cloud.PointCloudDefinition definition, Cloud.PointCloudInfo? info,
            ObjectId id, ObjectId definitionId, Entity resident, DocumentState state, string instance, object? bounds, string scope)
        {
            Insertion = insertion; _definition = definition; _resident = resident; Info = info; Id = id; DefinitionId = definitionId; State = state;
            SourceRef = new(instance, state.Id, id.Handle.ToString());
            DefinitionRef = new(instance, state.Id, definitionId.Handle.ToString());
            Loaded = definition.isLoaded; Path = Text(definition.CloudFileName); Name = Text(definition.CloudName);
            _bounds = bounds; _scope = scope;
            Fingerprint = CurrentFingerprint(info);
        }

        internal static Source Open(ObjectId id, DocumentState state, Transaction tx, string instance, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Cloud.PointCloudBridgeMdg.isPointCloudInsertion(id.OldIdPtr))
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Source is not a point cloud insertion.");
            if (tx.GetObject(id, OpenMode.ForRead) is not Entity resident || resident.ObjectId != id)
                throw new DomainException("OBJECT_IDENTITY_MISMATCH", "Cloud insertion resident identity mismatch.");
            var insertion = Cloud.PointCloudBridgeMdg.asPointCloudInsertion(id.OldIdPtr, Cloud.OpenModeMdg.kForRead);
            Cloud.PointCloudDefinition? definition = null;
            Cloud.PointCloudInfo? info = null;
            try
            {
                if (insertion == null || insertion.ObjectId != id)
                    throw new DomainException("OBJECT_IDENTITY_MISMATCH", "Cloud insertion wrapper identity mismatch.");
                var definitionId = insertion.PointCloudDefinitionID;
                if (definitionId.IsNull || !definitionId.IsValid || definitionId.IsErased || definitionId.IsEffectivelyErased || definitionId.Database != state.Document.Database)
                    throw new DomainException("OBJECT_IDENTITY_MISMATCH", "Cloud definition is unavailable in this database.");
                var obj = tx.GetObject(definitionId, OpenMode.ForRead);
                definition = Cloud.PointCloudBridgeMdg.asPointCloudDefinitionByObject(obj.UnmanagedObject, Cloud.OpenModeMdg.kForRead);
                if (definition == null || definition.ObjectId != definitionId)
                    throw new DomainException("OBJECT_IDENTITY_MISMATCH", "Cloud definition wrapper identity mismatch.");
                cancellation.ThrowIfCancellationRequested();
                object? bounds = null;
                var scope = "CloudInfo default-parameter metadata is an observation, not a verified raw-point total. Insertion bounds are native GeometricExtents in WCS; coordinate_system does not describe stored feature geometry. Transformation/back_transformation are native CloudInfo observations, not an instruction to transform the insertion iterator Vertex again.";
                if (definition.isLoaded)
                {
                    info = insertion.CloudInfo();
                    // In particular, a zero metadata counter is never used as an empty-cloud guard.
                    try
                    {
                        var extents = insertion.GeometricExtents;
                        bounds = new { min = Point(extents.MinPoint), max = Point(extents.MaxPoint) };
                    }
                    catch (Teigha.Runtime.Exception exception)
                    {
                        scope += " Insertion bounds are unavailable: " + exception.ErrorStatus + ".";
                    }
                }
                else scope += " Source is not already loaded; no load or metadata update was requested.";
                cancellation.ThrowIfCancellationRequested();
                return new Source(insertion, definition, info, id, definitionId, resident, state, instance, bounds, scope);
            }
            catch
            {
                info?.Dispose(); definition?.Dispose(); insertion?.Dispose();
                throw;
            }
        }

        // The SDK All factory is non-const. Its technical open-mode lease is restricted
        // to this already-resolved resident insertion. There are no cloud setters or
        // metadata updates, and the surrounding read transaction is never committed.
        // Restore the acquired mode before any point traversal; do not reopen by ID.
        internal Cloud.PointCloudVertexIterator AllIterator(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (_resident.ObjectId != Id || Insertion.ObjectId != Id)
                throw new DomainException("OBJECT_IDENTITY_MISMATCH", "Iterator factory source identity mismatch.");
            bool acquired = false;
            Cloud.PointCloudVertexIterator? iterator = null;
            try
            {
                if (!NativeRead("iterator_factory_open_mode", () => _resident.IsWriteEnabled))
                {
                    NativeRead("iterator_factory_upgrade", () => _resident.UpgradeOpen());
                    acquired = true;
                    PointCloudReadScope402.LeaseAcquired();
                }
                iterator = NativeRead("iterator_creation", () => Insertion.AllVertexIterator(0, false, false));
                return iterator ?? throw new DomainException("POINT_CLOUD_DATA_UNAVAILABLE", "Native All iterator is unavailable.");
            }
            finally
            {
                if (acquired)
                {
                    try
                    {
                        NativeRead("iterator_factory_downgrade", () => _resident.DowngradeOpen());
                        PointCloudReadScope402.LeaseRestored();
                    }
                    catch (System.Exception modeFailure)
                    {
                        // A returned iterator must not escape if restoration failed.
                        try { if (iterator != null) NativeRead("iterator_release_after_mode_failure", iterator.Dispose); }
                        catch (System.Exception releaseFailure)
                        {
                            throw new DomainException("POINT_CLOUD_ACCESS_RESTORE_FAILED", "Iterator access mode and release could not be completed.", details: new
                            { native_stage = "iterator_factory_downgrade", mode_error = modeFailure.GetType().Name,
                                release_error = releaseFailure.GetType().Name, update_on_releasing = false, cloud_setters_called = false });
                        }
                        throw;
                    }
                }
            }
        }

        private string CurrentFingerprint(Cloud.PointCloudInfo? info) => Digest(new
        {
            insertion = Id.Handle.ToString(), definition = DefinitionId.Handle.ToString(), loaded = _definition.isLoaded,
            path = Text(_definition.CloudFileName), name = Text(_definition.CloudName),
            data = info == null ? null : (object)new
            {
                count = Decimal(info.PointsCount), nodes = info.NodesCount, depth = info.TreeDepth,
                scheme = Decimal(info.DataScheme), encoder = info.DataEncoderState,
                transformation = Matrix((Matrix3d)info.Transformation), back = Matrix((Matrix3d)info.BackTransformation)
            }
        });

        internal void RequireLoaded()
        {
            if (!Loaded) throw new DomainException("POINT_CLOUD_NOT_LOADED", "Cloud data is not already loaded; this operation does not load or alter the source.");
        }

        internal void Verify(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Insertion.ObjectId != Id || Insertion.PointCloudDefinitionID != DefinitionId || _definition.ObjectId != DefinitionId)
                throw new DomainException("POINT_CLOUD_SOURCE_CHANGED", "The cloud insertion or definition identity changed during this read.");
            using var current = Loaded ? Insertion.CloudInfo() : null;
            if (CurrentFingerprint(current) != Fingerprint)
                throw new DomainException("POINT_CLOUD_SOURCE_CHANGED", "Native source metadata or its transformation changed during this read; obtain fresh context before another query.");
        }

        // Numeric/identity evidence only; no resident wrapper survives the request.
        // Bounds availability is mandatory only for coalescing a technical notification.
        internal string? ObservationProof => _bounds == null ? null : Digest(new
        { Fingerprint, owner = _resident.OwnerId.Handle.ToString(), bounds = _bounds });
        internal string? VerifyObservationProof(CancellationToken cancellation)
        {
            Verify(cancellation);
            if (_bounds == null) return null;
            var extents = Insertion.GeometricExtents;
            object bounds = new { min = Point(extents.MinPoint), max = Point(extents.MaxPoint) };
            return Digest(new { Fingerprint, owner = _resident.OwnerId.Handle.ToString(), bounds });
        }

        internal object Metadata(string mode, IReadOnlyList<FeatureRecord>? features, bool? complete, string? nextCursor) => new
        {
            mode, source_ref = SourceRef, definition_ref = DefinitionRef, loaded = Loaded, source_path = Path, cloud_name = Name,
            point_count = Info == null ? null : Decimal(Info.PointsCount), insertion_bounds = _bounds,
            coordinate_system = "WCS", units = new { name = State.Document.Database.Insunits.ToString(), value = (int)State.Document.Database.Insunits },
            source_units_known = false, source_units_note = SourceUnitsNote,
            data_scope = _scope, data_scheme = Info == null ? null : Decimal(Info.DataScheme),
            has_intensity = Info == null ? (bool?)null : Info.IsDataValid(2),
            has_classification = Info == null ? (bool?)null : Info.IsDataValid(4),
            has_rgb = Info == null ? (bool?)null : Info.IsDataValid(256),
            tree_depth = Info == null ? (int?)null : Info.TreeDepth,
            transformation = Info == null ? null : Matrix((Matrix3d)Info.Transformation),
            back_transformation = Info == null ? null : Matrix((Matrix3d)Info.BackTransformation),
            features, features_geometry_scope = FeatureScope, complete, next_cursor = nextCursor
        };

        public void Dispose()
        {
            using var phase = PointCloudReadScope402.Stage("point_cloud_source_release");
            // Keep release order local to this callback; the transaction owns resident DB objects.
            try { Info?.Dispose(); }
            finally { try { _definition.Dispose(); } finally { Insertion.Dispose(); } }
        }
    }

    internal sealed record FeatureGeometry(string coordinate_frame = "native_feature", double[]? origin = null,
        double[]? normal = null, double[]? center = null, double[]? guide = null, double[]? start = null,
        double[]? end = null, double[]? point = null, double? radius = null, double? radius1 = null,
        double? radius2 = null, double? height = null, double? major_radius = null, double? minor_radius = null,
        uint[]? plane_ids = null);
    internal sealed record FeatureRecord(uint feature_id, string kind, int native_type, string? name,
        string population, double deviation, FeatureGeometry? geometry);
}
