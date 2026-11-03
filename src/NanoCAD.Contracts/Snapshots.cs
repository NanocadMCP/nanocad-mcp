using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace NanoCAD.Contracts;

/// <summary>UI-owned snapshots contain immutable DTOs only. Cursor tokens reveal no handles or paths.</summary>
public sealed record SnapshotPage<T>(T[] items, int total_count, int returned_count, bool complete, string? next_cursor, string? selection_revision);

public sealed class SnapshotPages<T>
{
    private sealed record Snapshot(string Id, string DocumentId, string Revision, string? SelectionRevision,
        string Filter, T[] Items, DateTime Expires);
    private readonly Dictionary<string, Snapshot> _snapshots = new();
    private readonly byte[] _cursorKey = RandomNumberGenerator.GetBytes(32);
    private readonly Func<DateTime> _now;
    private readonly Func<T, string> _handle;
    public SnapshotPages(Func<T, string> handle, Func<DateTime>? now = null) { _handle = handle; _now = now ?? (() => DateTime.UtcNow); }
    public void Clear() => _snapshots.Clear();
    public SnapshotPage<T> First(string documentId, string revision, string? selectionRevision, string filter, T[] items, int pageSize)
    {
        Expire();
        if (JsonSerializer.SerializeToUtf8Bytes(items, Wire.Json).Length > 8 * 1024 * 1024)
            throw new DomainException("QUERY_LIMIT_EXCEEDED", "Snapshot exceeds its actual 8MiB serialized-byte budget; narrow the query.");
        while (_snapshots.Count >= 2) Remove(_snapshots.Values.OrderBy(s => s.Expires).First().Id);
        var snapshot = new Snapshot(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), documentId, revision, selectionRevision, filter,
            items.OrderBy(i => ulong.Parse(_handle(i), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray(), _now().AddSeconds(60));
        _snapshots.Add(snapshot.Id, snapshot);
        return Slice(snapshot, 0, pageSize);
    }
    public SnapshotPage<T> Next(string cursor, string documentId, string revision, string? selectionRevision, string filter, int pageSize)
    {
        Expire();
        if (cursor.Length != 104 || !cursor.All(Uri.IsHexDigit)) throw new DomainException("INVALID_CURSOR", "Malformed cursor.");
        var raw = Convert.FromHexString(cursor[..40]);
        using var hmac = new HMACSHA256(_cursorKey);
        if (!CryptographicOperations.FixedTimeEquals(hmac.ComputeHash(raw), Convert.FromHexString(cursor[40..])))
            throw new DomainException("INVALID_CURSOR", "Cursor signature is invalid.");
        string id = cursor[..32].ToUpperInvariant();
        int offset = checked((int)uint.Parse(cursor.Substring(32, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        if (!_snapshots.TryGetValue(id, out var snapshot)) throw new DomainException("CURSOR_EXPIRED", "Snapshot expired/evicted; start a new query.");
        if (snapshot.DocumentId != documentId || snapshot.Filter != filter) throw new DomainException("INVALID_CURSOR", "Document or filters differ from the original cursor request.");
        if (snapshot.Revision != revision) throw new DomainException("STALE_DOCUMENT", "The document changed since this snapshot.", true);
        if (snapshot.SelectionRevision != selectionRevision) throw new DomainException("STALE_SELECTION", "The implied selection changed since this snapshot.", true);
        if (offset < 0 || offset >= snapshot.Items.Length) throw new DomainException("INVALID_CURSOR", "Invalid cursor position.");
        return Slice(snapshot, offset, pageSize);
    }
    private SnapshotPage<T> Slice(Snapshot snapshot, int offset, int pageSize)
    {
        var page = new List<T>();
        int bytes = 2048;
        foreach (var item in snapshot.Items.Skip(offset).Take(pageSize))
        {
            int size = JsonSerializer.SerializeToUtf8Bytes(item, Wire.Json).Length + 1;
            if (bytes + size > Wire.PayloadBudget - 4096) break;
            page.Add(item); bytes += size;
        }
        if (page.Count == 0 && offset < snapshot.Items.Length) throw new DomainException("ENTITY_TOO_LARGE", "An entity summary exceeds the response budget.");
        int next = offset + page.Count;
        string? cursor = null;
        if (next < snapshot.Items.Length)
        {
            string prefix = snapshot.Id + next.ToString("X8", CultureInfo.InvariantCulture);
            using var hmac = new HMACSHA256(_cursorKey);
            cursor = prefix + Convert.ToHexString(hmac.ComputeHash(Convert.FromHexString(prefix)));
        }
        return new(page.ToArray(), snapshot.Items.Length, page.Count, cursor == null, cursor, snapshot.SelectionRevision);
    }
    private void Expire() { foreach (var id in _snapshots.Values.Where(s => s.Expires <= _now()).Select(s => s.Id).ToArray()) Remove(id); }
    private void Remove(string id)
    {
        _snapshots.Remove(id);
    }
}

// Preserve the Phase 1 pagination contract and its public API.
public sealed class Snapshots
{
    private readonly SnapshotPages<EntitySummary> _pages;
    public Snapshots(Func<DateTime>? now = null) => _pages = new(i => i.@ref.handle, now);
    public void Clear() => _pages.Clear();
    private static Page Convert(SnapshotPage<EntitySummary> p) => new(p.items, p.total_count, p.returned_count, p.complete, p.next_cursor, p.selection_revision);
    public Page First(string doc, string revision, string? selection, string filter, EntitySummary[] items, int size) => Convert(_pages.First(doc, revision, selection, filter, items, size));
    public Page Next(string cursor, string doc, string revision, string? selection, string filter, int size) => Convert(_pages.Next(cursor, doc, revision, selection, filter, size));
}
