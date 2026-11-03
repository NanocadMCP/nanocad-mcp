using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace NanoCAD.Contracts;

// A drawing-size-independent page cache: store compact identities, materialize only
// the requested page. The reader callback is invoked synchronously on the CAD thread.
public sealed class HandlePages<T> where T:class
{
    private sealed record Snapshot(string Id,string Document,string Revision,string? Selection,string Filter,ulong[] Handles,DateTime Expires);
    private readonly Dictionary<string,Snapshot> _snapshots=new();
    private readonly byte[] _key=RandomNumberGenerator.GetBytes(32);
    private readonly Func<DateTime> _now;
    public HandlePages(Func<DateTime>? now=null)=>_now=now??(()=>DateTime.UtcNow);
    public sealed record Page(T[] items,int? total_count,int returned_count,bool complete,string? next_cursor,string? selection_revision);
    public Page First(string document,string revision,string? selection,string filter,IEnumerable<ulong> handles,int size,Func<ulong,T?> read,Func<bool>? yield=null)
    {
        Expire();while(_snapshots.Count>=2)_snapshots.Remove(_snapshots.Values.OrderBy(s=>s.Expires).First().Id);
        var ids=handles.ToArray();Array.Sort(ids);
        var snapshot=new Snapshot(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),document,revision,selection,filter,ids,_now().AddMinutes(2));
        _snapshots.Add(snapshot.Id,snapshot);return Slice(snapshot,0,0,size,read,yield);
    }
    public Page Next(string cursor,string document,string revision,string? selection,string filter,int size,Func<ulong,T?> read,Func<bool>? yield=null)
    {
        Expire();
        if(cursor.Length!=112||!cursor.All(Uri.IsHexDigit))throw new DomainException("INVALID_CURSOR","Malformed cursor.");
        using var mac=new HMACSHA256(_key);
        if(!CryptographicOperations.FixedTimeEquals(mac.ComputeHash(Convert.FromHexString(cursor[..48])),Convert.FromHexString(cursor[48..])))
            throw new DomainException("INVALID_CURSOR","Cursor signature is invalid.");
        if(!_snapshots.TryGetValue(cursor[..32].ToUpperInvariant(),out var snapshot))throw new DomainException("CURSOR_EXPIRED","Query cursor expired; start a new query.");
        if(snapshot.Document!=document||snapshot.Filter!=filter)throw new DomainException("INVALID_CURSOR","Document or filters changed.");
        if(snapshot.Revision!=revision)throw new DomainException("STALE_DOCUMENT","Drawing changed since the first page.",true);
        if(snapshot.Selection!=selection)throw new DomainException("STALE_SELECTION","Selection changed since the first page.",true);
        var offset=uint.Parse(cursor.Substring(32,8),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        var matched=uint.Parse(cursor.Substring(40,8),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        if(offset>=snapshot.Handles.Length||matched>offset)throw new DomainException("INVALID_CURSOR","Invalid cursor position.");
        snapshot=snapshot with{Expires=_now().AddMinutes(2)};_snapshots[snapshot.Id]=snapshot;
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
                int length=JsonSerializer.SerializeToUtf8Bytes(item,Wire.Json).Length+1;
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
    private void Expire(){foreach(var s in _snapshots.Values.Where(s=>s.Expires<=_now()).ToArray())_snapshots.Remove(s.Id);}
}
