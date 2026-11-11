using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Read-only, exact metadata snapshot. Follow native ownership, not arbitrary
// referenced entities; a soft reference is retained as an identity, not erased
// or treated as the root's private data.
internal static class NativeOwnedMetadata
{
    internal const string NativeProxyWarning = "NATIVE_PROXY_PRIVATE_STATE_UNOBSERVED: A verified native part/construction owns proxy storage. Current local identity/owner paths, runtime and original class/DXF/application/flags, readable public reference identities/states, XData/reactors/extensions and ordinary owned records remain observed and checked. Unavailable proxy references stay explicit. The SDK exposes no proxy-private payload getter here; private bytes are not claimed fully observed or preserved. This adapter reads only and does not authorize erasing, resurrecting or editing proxy data or its referenced objects.";
    internal const string NativeProxyReferencesWarning = "NATIVE_PROXY_REFERENCES_UNRESOLVED: Some stored proxy references or their inventory are unavailable, erased, null or outside the current database. Their observed states and any readable identities are retained, without traversal or a healthy-reference claim. This observation alone does not establish that the part's actual native history/geometry is broken; those identities, controls and relationships are checked separately.";
    private sealed record ProxyReference(string? handle, int kind, string? owner_handle, string? runtime_type,
        string? raw_id_token, string state, bool? is_null, bool? is_valid, bool? is_erased, bool? is_effectively_erased,
        string database_scope, string? rx_class_name, string? observation_error);
    private static string? Id(ObjectId id) => id.IsNull ? null : Validation.NormalizeHandle(id.Handle.ToString());
    internal static object? Value(object? value) => value switch
    {
        null => null,
        string or bool or byte or sbyte or short or ushort or int or uint or long or ulong => value,
        double number => new { native_type = "double", bits = BitConverter.DoubleToInt64Bits(number).ToString("X16") },
        float number => new { native_type = "float", bits = BitConverter.SingleToInt32Bits(number).ToString("X8") },
        byte[] bytes => new { native_type = "binary", data = Convert.ToBase64String(bytes) },
        Handle handle => NativeMetadataValues.Handle(handle.Value),
        ObjectId id => new { native_type = "ObjectId", handle = Id(id) },
        Point2d point => new { native_type = "Point2d", coordinates = new[] { Value(point.X), Value(point.Y) } },
        Point3d point => new { native_type = "Point3d", coordinates = new[] { Value(point.X), Value(point.Y), Value(point.Z) } },
        Vector2d vector => new { native_type = "Vector2d", coordinates = new[] { Value(vector.X), Value(vector.Y) } },
        Vector3d vector => new { native_type = "Vector3d", coordinates = new[] { Value(vector.X), Value(vector.Y), Value(vector.Z) } },
        _ => throw new DomainException("UNSUPPORTED_OWNED_METADATA", "A native metadata value needs its owning adapter; no unclassified value was omitted.", false, new { runtime_type = value.GetType().FullName })
    };
    internal static object[] Buffer(ResultBuffer? buffer, Action cancel, Action<object> add, string path = "buffer")
    {
        var items = new List<object>(); int index = 0;
        if (buffer != null) foreach (var value in buffer.AsArray())
        {
            cancel(); object? observed;
            try { observed = Value(value.Value); }
            catch (DomainException error) when (error.Error.code == "UNSUPPORTED_OWNED_METADATA")
            {
                throw new DomainException(error.Error.code, error.Error.message, error.Error.retryable,
                    new { runtime_type = value.Value?.GetType().FullName, type_code = value.TypeCode, value_path = path + "[" + index + "].value", cause = error.Error.details });
            }
            var item = new { code = value.TypeCode, value = observed }; add(item); items.Add(item); index++;
        }
        return items.ToArray();
    }
    internal static object NativeProxyObservation(ProxyObject proxy, DBObject root, Transaction tr, Action cancel, Action<object> add)
    {
        void Need(bool value, string message) { if (!value) throw new DomainException("UNSUPPORTED_OWNED_METADATA", message); }
        cancel(); Need(proxy.GetType() == typeof(ProxyObject) && !proxy.ObjectId.IsNull && proxy.ObjectId.IsValid && !proxy.ObjectId.IsErased && !proxy.ObjectId.IsEffectivelyErased &&
            proxy.Database?.UnmanagedObject == root.Database.UnmanagedObject, "Native proxy observation requires an exact current local resident object.");
        DBObjectReferenceCollection? references = null; string? inventoryError = null;
        try { references = proxy.GetReferences(); }
        catch (Teigha.Runtime.Exception error) { inventoryError = error.ErrorStatus.ToString(); }
        catch (NotSupportedException) { inventoryError = "NotSupportedException"; }
        var observed = new List<ProxyReference>();
        if (references != null) foreach (DBObjectReference reference in references)
        {
            cancel(); var id = reference.ObjectId;
            string? handle = null, token = null, owner = null, runtime = null, rxClass = null; bool? isNull = null, valid = null, erased = null, effectivelyErased = null;
            string database = "unavailable"; var errors = new HashSet<string>(StringComparer.Ordinal);
            // These are independent observations of a stored proxy ID. The
            // SDK exposes no guarantee that every returned reference is live.
            // Do not open erased/foreign targets or infer ownership from Kind.
            void Read(Action getter)
            {
                cancel(); try { getter(); }
                catch (Teigha.Runtime.Exception error) { errors.Add(error.ErrorStatus.ToString()); }
                catch (NotSupportedException) { errors.Add("NotSupportedException"); }
            }
            Read(() => isNull = id.IsNull);
            if (isNull != true)
            {
                Read(() => token = id.OldIdPtr.ToInt64().ToString("X16"));
                Read(() => valid = id.IsValid);
                if (valid == true)
                {
                    Read(() => handle = Id(id));
                    Read(() => erased = id.IsErased); Read(() => effectivelyErased = id.IsEffectivelyErased);
                    Read(() => database = id.Database == null ? "unavailable" : id.Database.UnmanagedObject == root.Database.UnmanagedObject ? "local" : "foreign");
                }
            }
            string state = isNull == true ? "null" : valid == false ? "invalid" : valid == null ? "identity_unavailable" :
                erased == true ? "erased" : effectivelyErased == true ? "effectively_erased" : erased == null || effectivelyErased == null ? "state_unavailable" :
                database == "foreign" ? "foreign_database" : database != "local" ? "database_unavailable" : "live_local";
            if (state == "live_local")
            {
                DBObject? target = null; Read(() => target = tr.GetObject(id, OpenMode.ForRead));
                if (target == null) state = "target_read_unavailable";
                else
                {
                    // A readable live-local target must still be this exact
                    // object. An identity mismatch remains a hard failure.
                    Need(target.ObjectId == id && target.Database?.UnmanagedObject == root.Database.UnmanagedObject,
                        "A readable native proxy reference lost its exact current local database identity.");
                    Read(() => owner = Id(target.OwnerId)); runtime = target.GetType().FullName;
                    // ObjectId.ObjectClass implicitly opens its target in the
                    // installed SDK. Query only this already-open local object.
                    Read(() => rxClass = target.GetRXClass().Name);
                }
            }
            var value = new ProxyReference(handle, reference.Kind, owner, runtime, token, state, isNull, valid, erased, effectivelyErased,
                database, rxClass, errors.Count == 0 ? null : string.Join("|", errors.OrderBy(value => value, StringComparer.Ordinal)));
            add(value); observed.Add(value);
        }
        var rx = proxy.GetRXClass();
        return new
        {
            adapter = "resident_native_proxy_anchor421",
            private_state_complete = false,
            rx_class_name = rx.Name,
            rx_dxf_name = rx.DxfName,
            original_class_name = proxy.OriginalClassName,
            original_dxf_name = proxy.OriginalDxfName,
            application_description = proxy.ApplicationDescription,
            proxy_flags = proxy.ProxyFlags,
            reference_inventory_complete = references != null,
            reference_inventory_error = inventoryError,
            reference_targets_complete = references != null && observed.All(value => value.state == "live_local" && value.observation_error == null),
            references = references == null ? null : observed.OrderBy(value => value.raw_id_token, StringComparer.Ordinal).ThenBy(value => value.kind)
                .ThenBy(value => value.handle, StringComparer.Ordinal).ThenBy(value => value.state, StringComparer.Ordinal)
                .ThenBy(value => value.owner_handle, StringComparer.Ordinal).ThenBy(value => value.runtime_type, StringComparer.Ordinal)
                .ThenBy(value => value.observation_error, StringComparer.Ordinal).ToArray(),
            reference_identity_scope = "current_process_ObjectId.OldIdPtr_when_readable; no_persistent_or_owned_target_claim",
            observation_scope = "exact_local_owned_proxy_identity_and_reported_public_reference_states; private_native_payload_unobserved"
        };
    }
    internal static JsonElement Capture(DBObject root, Transaction tr, Action cancel, bool observeAssocManager = false, IReadOnlyDictionary<ObjectId, JsonElement>? tableFormulaFields = null, bool observeNativeProxy = false, string? observationPath = null)
    {
        var records = new List<object>(); long bytes = 2048;
        void Need(bool condition, string message)
        { if (!condition) throw new DomainException("UNSUPPORTED_OWNED_METADATA", message); }
        void Add(object item)
        {
            cancel(); bytes += JsonSerializer.SerializeToUtf8Bytes(item, Wire.Json).Length + 1;
            if (bytes > 8 * 1024 * 1024) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Owned native metadata exceeds the actual8MiB serialized snapshot budget; no record count was truncated.");
        }
        cancel(); string rootPath = observationPath ?? "native_metadata[" + Id(root.ObjectId) + "]";
        var pending = new Stack<(ObjectId Id, ObjectId Owner)>(); var visited = new HashSet<ObjectId>();
        if (!root.ExtensionDictionary.IsNull) pending.Push((root.ExtensionDictionary, root.ObjectId));
        while (pending.Count > 0)
        {
            long recordStart = bytes;
            cancel(); var next = pending.Pop(); Need(!next.Id.IsNull && next.Id.IsValid && !next.Id.IsErased && !next.Id.IsEffectivelyErased, "An owned native metadata identity is unavailable.");
            Need(visited.Add(next.Id), "Owned native metadata contains a repeated ownership path or a cycle.");
            var item = tr.GetObject(next.Id, OpenMode.ForRead);
            Need(item.Database == root.Database && item.OwnerId == next.Owner, "Owned native metadata has inconsistent document/owner identity.");
            bool boundFormula = item.GetType() == typeof(Field) && tableFormulaFields != null && tableFormulaFields.ContainsKey(item.ObjectId);
            Need(!item.HasFields || boundFormula, "Field-bearing owned metadata requires its owning field adapter.");
            using var xdata = item.XData; using var reactors = item.GetPersistentReactorIds();
            var links = new List<string?>(); foreach (ObjectId link in reactors) { cancel(); Add(Id(link)!); links.Add(Id(link)); }
            var common = new
            {
                handle = Id(item.ObjectId),
                owner_handle = Id(item.OwnerId),
                runtime_type = item.GetType().FullName,
                extension_handle = Id(item.ExtensionDictionary),
                xdata = xdata == null ? null : Buffer(xdata, cancel, Add, rootPath + ".owned[" + Id(item.ObjectId) + "].xdata"),
                reactors = links.OrderBy(value => value, StringComparer.Ordinal).ToArray()
            };
            object record;
            if (item.GetType() == typeof(DBDictionary) && item is DBDictionary dictionary)
            {
                var entries = new List<object>(); var owned = new List<(string Name, ObjectId Id)>();
                foreach (DBDictionaryEntry entry in dictionary)
                {
                    cancel(); Need(!entry.Value.IsNull && entry.Value.IsValid && !entry.Value.IsErased && !entry.Value.IsEffectivelyErased, "A native dictionary reference is unavailable.");
                    var child = tr.GetObject(entry.Value, OpenMode.ForRead);
                    bool owns = child.Database == root.Database && child.OwnerId == item.ObjectId;
                    var descriptor = new { name = entry.Key, handle = Id(entry.Value), owner_handle = Id(child.OwnerId), runtime_type = child.GetType().FullName, owned = owns };
                    Add(descriptor); entries.Add(descriptor); if (owns) owned.Add((entry.Key, entry.Value));
                }
                foreach (var child in owned.OrderByDescending(value => value.Name, StringComparer.Ordinal)) pending.Push((child.Id, item.ObjectId));
                // Keep enumeration order in the snapshot: it is an observable stored
                // native dictionary state, not a geometric number to normalize.
                record = new { common, dictionary_hard_owned = dictionary.TreatElementsAsHard, entries };
            }
            else if (item.GetType() == typeof(Xrecord) && item is Xrecord xrecord)
            {
                using var data = xrecord.Data;
                record = new { common, xlate_references = xrecord.XlateReferences, merge_style = xrecord.MergeStyle.ToString(), data = data == null ? null : Buffer(data, cancel, Add, rootPath + ".owned[" + Id(item.ObjectId) + "].data") };
            }
            else if (boundFormula)
                record = new { common, table_formula_field = tableFormulaFields![item.ObjectId] };
            else if (observeAssocManager && item.GetType() == typeof(AssocManager) && item is AssocManager manager)
                record = new { common, assoc_manager_observation = NativeAssocManagerObservation356.Capture(manager, root, cancel) };
            else if (observeNativeProxy && item.GetType() == typeof(ProxyObject) && item is ProxyObject proxy)
            {
                // Explicit native-part callers have proved the root identity.
                // The ordinary owner walker still proves every path to it.
                // Proxy references are identities, never additional owned data.
                record = new { common, native_proxy_observation = NativeProxyObservation(proxy, root, tr, cancel, Add) };
            }
            else throw new DomainException("UNSUPPORTED_OWNED_METADATA", "Native owned data needs its dedicated adapter; ordinary dictionaries/Xrecords remain supported.", false, new { runtime_type = item.GetType().FullName });
            if (!item.ExtensionDictionary.IsNull) pending.Push((item.ExtensionDictionary, item.ObjectId));
            // Incremental accounting bounds construction. Replace reservations
            // with the final record bytes so child descriptors are not counted
            // a second time merely because they sit inside this same record.
            bytes = recordStart; Add(record); records.Add(record);
        }
        var result = Wire.Element(records);
        if (JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length > 8 * 1024 * 1024) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Owned native metadata exceeds the actual8MiB serialized snapshot budget.");
        return result;
    }
    internal static string[] NativeProxyWarnings(params JsonElement[] observations)
    {
        var pending = new Stack<JsonElement>(observations); bool proxy = false, unresolved = false;
        while (pending.Count > 0)
        {
            var value = pending.Pop();
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("native_proxy_observation", out var observed))
                {
                    proxy = true;
                    if (observed.TryGetProperty("reference_targets_complete", out var complete) && complete.ValueKind == JsonValueKind.False) unresolved = true;
                }
                foreach (var field in value.EnumerateObject()) pending.Push(field.Value);
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) pending.Push(item);
        }
        return unresolved ? [NativeProxyWarning, NativeProxyReferencesWarning] : proxy ? [NativeProxyWarning] : [];
    }
}
