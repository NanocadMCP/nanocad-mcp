using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

// Diagnostics only: tokens and admission remain unchanged. Native database
// identity survives newly allocated managed Database wrappers. The process-wide
// byte-bounded ledger stores no native objects or drawing strings, only identities
// and complete field SHA256 fingerprints. An exact token still binds a comparison.
internal static class CadTableSnapshotDiagnostics346
{
    private const int ObservationBytes=2*1024*1024,CacheBytes=8*1024*1024,DetailsBytes=8192;
    private sealed record Field(string path,string sha256);
    private sealed record Difference(string path,string? expected_sha256,string? current_sha256);
    private sealed record Observation(string Token,Dictionary<string,string>? Fields,string? Reason,int Bytes);
    private readonly record struct Key(IntPtr Database,string Identity);
    private sealed record Entry(Observation Value,LinkedListNode<Key> Order,int Bytes);
    private sealed class Cache
    {
        internal readonly object Gate=new();
        internal readonly Dictionary<Key,Entry> Entries=new();
        internal readonly LinkedList<Key> Order=new();
        internal int Bytes;
    }
    private static readonly Cache Ledger=new();

    private static Observation Fingerprints(Snapshot snapshot,Action cancel)
    {
        var fields=new Dictionary<string,string>(StringComparer.Ordinal);int bytes=256;bool exhausted=false;
        void Add(string path,object? value)
        {
            cancel();if(exhausted)return;
            var hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
            int size=JsonSerializer.SerializeToUtf8Bytes(new Field(path,hash),Wire.Json).Length+1;
            if((long)bytes+size>ObservationBytes){exhausted=true;fields.Clear();return;}
            bytes+=size;fields.Add(path,hash);
        }
        void Properties(string path,JsonElement value)
        {
            cancel();
            if(value.ValueKind!=JsonValueKind.Object){Add(path,value);return;}
            foreach(var property in value.EnumerateObject())
            {
                cancel();if(exhausted)return;
                if(property.Name=="format"&&property.Value.ValueKind==JsonValueKind.Object)
                    Properties(path+"/format",property.Value);
                else if(property.Name=="grids"&&property.Value.ValueKind==JsonValueKind.Array)
                {
                    int index=0;
                    foreach(var grid in property.Value.EnumerateArray())
                    {cancel();Add(path+"/grids/"+index++,grid);if(exhausted)return;}
                }
                else Add(path+"/"+property.Name,property.Value);
            }
        }
        Add("/identity",snapshot.Identity);Add("/rows",snapshot.Rows);Add("/columns",snapshot.Columns);
        foreach(var property in snapshot.Common.EnumerateObject())
        {
            cancel();if(exhausted)break;
            if(property.Name=="row_defaults"&&property.Value.ValueKind==JsonValueKind.Array)
            {
                int index=0;
                foreach(var row in property.Value.EnumerateArray())
                {cancel();Properties("/common/row_defaults/"+index++,row);if(exhausted)break;}
            }
            else if(property.Name=="properties")Properties("/common/properties",property.Value);
            else Add("/common/"+property.Name,property.Value);
        }
        for(int index=0;index<snapshot.RowHeights.Length&&!exhausted;index++)
            Add("/row_heights/"+index,snapshot.RowHeights[index]);
        for(int index=0;index<snapshot.ColumnWidths.Length&&!exhausted;index++)
            Add("/column_widths/"+index,snapshot.ColumnWidths[index]);
        for(int index=0;index<snapshot.Cells.Length&&!exhausted;index++)
        {
            cancel();Properties("/cells/"+index,JsonSerializer.SerializeToElement(snapshot.Cells[index],Wire.Json));
            if(exhausted)break;
            Properties("/native_contents/"+index,JsonSerializer.SerializeToElement(snapshot.Values[index],Wire.Json));
            if(exhausted)break;
            Add("/cell_overrides/"+index,snapshot.CellOverrides[index]);
        }
        if(exhausted)return new(snapshot.Token,null,"Complete field fingerprints exceed the diagnostic byte budget; no partial field inventory is claimed.",256);
        return new(snapshot.Token,fields,null,bytes);
    }

    internal static void Observe(Table table,Snapshot snapshot,Action cancel)
    {
        cancel();if(table.ObjectId.IsNull||table.Database is not {} database)return;
        var key=new Key(database.UnmanagedObject,snapshot.Identity);if(key.Database==IntPtr.Zero)return;
        var observed=Fingerprints(snapshot,cancel);cancel();
        int storedBytes=JsonSerializer.SerializeToUtf8Bytes(new{database=key.Database.ToString("X"),identity=key.Identity,
            token=observed.Token,fields=observed.Fields,reason=observed.Reason},Wire.Json).Length;
        if(storedBytes>CacheBytes)return;
        var cache=Ledger;
        lock(cache.Gate)
        {
            if(cache.Entries.Remove(key,out var previous))
            {cache.Bytes-=previous.Bytes;cache.Order.Remove(previous.Order);}
            while((long)cache.Bytes+storedBytes>CacheBytes&&cache.Order.First is {} oldest)
            {
                cancel();var old=cache.Entries[oldest.Value];cache.Bytes-=old.Bytes;
                cache.Entries.Remove(oldest.Value);cache.Order.RemoveFirst();
            }
            cancel();var node=cache.Order.AddLast(key);
            cache.Entries.Add(key,new(observed,node,storedBytes));cache.Bytes+=storedBytes;
        }
    }

    internal static object Compare(Table table,Snapshot current,string expectedToken,Action cancel)
    {
        cancel();Observation? previous=null;
        if(!table.ObjectId.IsNull&&table.Database is {} database&&database.UnmanagedObject!=IntPtr.Zero)
            lock(Ledger.Gate)
                if(Ledger.Entries.TryGetValue(new(database.UnmanagedObject,current.Identity),out var found)&&found.Value.Token==expectedToken)
                    previous=found.Value;
        string? reason=previous==null?"The exact expected token has no retained last-public field fingerprints in this database.":previous.Reason;
        var changes=new List<Difference>();int changed=0;int bytes=1024;
        if(previous?.Fields is {} before)
        {
            var now=Fingerprints(current,cancel);
            if(now.Fields is {} after)
            {
                foreach(var path in before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).OrderBy(path=>path,StringComparer.Ordinal))
                {
                    cancel();before.TryGetValue(path,out var oldHash);after.TryGetValue(path,out var newHash);
                    if(oldHash==newHash)continue;
                    changed++;var difference=new Difference(path,oldHash,newHash);
                    int size=JsonSerializer.SerializeToUtf8Bytes(difference,Wire.Json).Length+1;
                    if((long)bytes+size<=DetailsBytes){changes.Add(difference);bytes+=size;}
                }
            }
            else reason=now.Reason;
        }
        return new{expected_table_token=expectedToken,current_table_token=current.Token,
            public_observation_available=reason==null,changed_field_count=changed,
            changed_fields=changes.ToArray(),omitted_changed_field_count=changed-changes.Count,
            diagnostic_unavailable_reason=reason};
    }
}
