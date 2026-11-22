using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

// A drawing-size-independent page cache: store compact identities, materialize only
// the requested page. The reader callback is invoked synchronously on the CAD thread.
public sealed class HandlePages<T> where T:class
{
    private sealed record Snapshot(string Id,string Document,string Revision,string? Selection,string Filter,ulong[] Handles,DateTime Expires);
    // This allowance is shared across retained queries in this cache. A single
    // larger drawing can always finish paging, regardless of its handle count.
    private readonly Dictionary<string, LinkedListNode<Snapshot>> _snapshots = new();
    private readonly LinkedList<Snapshot> _recent = new();
    private long _retainedBytes;
    private readonly Func<T, int> _measure;
    private readonly byte[] _key=RandomNumberGenerator.GetBytes(32);
    private readonly Func<DateTime> _now;
    public HandlePages(Func<DateTime>? now=null, Func<T,int>? measure=null)
    {
        _now = now ?? (() => DateTime.UtcNow);
        _measure = measure ?? (item => JsonSerializer.SerializeToUtf8Bytes(item, Wire.Json).Length);
    }
    public sealed record Page(T[] items,int? total_count,int returned_count,bool complete,string? next_cursor,string? selection_revision);
    public Page First(string document,string revision,string? selection,string filter,IEnumerable<ulong> handles,int size,Func<ulong,T?> read,Func<bool>? yield=null)
    {
        Expire();
        var ids=handles.ToArray();Array.Sort(ids);
        var snapshot=new Snapshot(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),document,revision,selection,filter,ids,_now().AddMinutes(2));
        var page = Slice(snapshot,0,0,size,read,yield);
        if (!page.complete)
        {
            long bytes = RetainedBytes(snapshot);
            while (_recent.First != null && _retainedBytes + bytes > SerializedCapacity357.SnapshotBytes)
                RemoveOldest();
            _snapshots.Add(snapshot.Id, _recent.AddLast(snapshot));
            _retainedBytes += bytes;
        }
        return page;
    }
    public Page Next(string cursor,string document,string revision,string? selection,string filter,int size,Func<ulong,T?> read,Func<bool>? yield=null)
    {
        Expire();
        if(cursor.Length!=112||!cursor.All(Uri.IsHexDigit))throw new DomainException("INVALID_CURSOR","Malformed cursor.");
        using var mac=new HMACSHA256(_key);
        if(!CryptographicOperations.FixedTimeEquals(mac.ComputeHash(Convert.FromHexString(cursor[..48])),Convert.FromHexString(cursor[48..])))
            throw new DomainException("INVALID_CURSOR","Cursor signature is invalid.");
        if(!_snapshots.TryGetValue(cursor[..32].ToUpperInvariant(),out var node))throw new DomainException("CURSOR_EXPIRED","Query cursor expired; start a new query.");
        var snapshot = node.Value;
        if(snapshot.Document!=document||snapshot.Filter!=filter)throw new DomainException("INVALID_CURSOR","Document or filters changed.");
        if(snapshot.Revision!=revision)throw new DomainException("STALE_DOCUMENT","Drawing changed since the first page.",true);
        if(snapshot.Selection!=selection)throw new DomainException("STALE_SELECTION","Selection changed since the first page.",true);
        var offset=uint.Parse(cursor.Substring(32,8),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        var matched=uint.Parse(cursor.Substring(40,8),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        if(offset>=snapshot.Handles.Length||matched>offset)throw new DomainException("INVALID_CURSOR","Invalid cursor position.");
        node.Value = snapshot with { Expires = _now().AddMinutes(2) };
        _recent.Remove(node);
        _recent.AddLast(node);
        return Slice(snapshot,(int)offset,(int)matched,size,read,yield);
    }
    private Page Slice(Snapshot snapshot,int offset,int matched,int size,Func<ulong,T?> read,Func<bool>? yield)
    {
        if(size<1)throw new ArgumentOutOfRangeException(nameof(size));
        var items=new List<T>();int bytes=2048,start=offset;
        while(offset<snapshot.Handles.Length&&items.Count<size)
        {
            if(offset>start&&yield?.Invoke()==true)break;
            var item=read(snapshot.Handles[offset]);
            if(item!=null)
            {
                int length=_measure(item)+1;
                if(bytes+length>Wire.PayloadBudget-4096)
                {
                    if(items.Count==0)throw new DomainException("ENTITY_TOO_LARGE","One entity summary exceeds the response byte budget.");
                    break;
                }
                bytes+=length;items.Add(item);matched++;
            }
            offset++;
        }
        bool complete=offset==snapshot.Handles.Length;string? cursor=null;
        if(!complete)
        {
            var prefix=snapshot.Id+offset.ToString("X8",CultureInfo.InvariantCulture)+matched.ToString("X8",CultureInfo.InvariantCulture);
            using var mac=new HMACSHA256(_key);cursor=prefix+Convert.ToHexString(mac.ComputeHash(Convert.FromHexString(prefix)));
        }
        return new(items.ToArray(),complete?matched:null,items.Count,complete,cursor,snapshot.Selection);
    }
    private static long RetainedBytes(Snapshot s) =>
        256L + s.Handles.LongLength * sizeof(ulong) +
        sizeof(char) * ((long)s.Id.Length + s.Document.Length + s.Revision.Length + (s.Selection?.Length ?? 0) + s.Filter.Length);

    private void RemoveOldest()
    {
        var snapshot = _recent.First!.Value;
        _recent.RemoveFirst();
        _snapshots.Remove(snapshot.Id);
        _retainedBytes -= RetainedBytes(snapshot);
    }

    private void Expire()
    {
        var now = _now();
        while (_recent.First is { } oldest && oldest.Value.Expires <= now) RemoveOldest();
    }
}

// The exact, already validated wire bytes used to charge an object page can be
// emitted directly while building its IPC envelope.
[JsonConverter(typeof(EncodedPageItem.Converter))]
public sealed class EncodedPageItem
{
    private readonly byte[] _utf8;
    private EncodedPageItem(byte[] utf8) => _utf8 = utf8;
    public static EncodedPageItem From<TItem>(TItem item) => new(JsonSerializer.SerializeToUtf8Bytes(item, Wire.Json));
    public int Length => _utf8.Length;

    public sealed class Converter : JsonConverter<EncodedPageItem>
    {
        public override void Write(Utf8JsonWriter writer, EncodedPageItem value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value._utf8, skipInputValidation: true);

        public override EncodedPageItem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("Page items are serialized for output only.");
    }
}
