using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using NanoCAD.Contracts;
using static NanoCADBridge.CadDrawingViewEditState;

namespace NanoCADBridge;

// Completed snapshots only. A multi-source projection can cease to be an
// item of one part, but that transition must be proved across both inventories.
// This adapter never assigns a host/order or removes a native relationship.
internal static class DrawingViewSourceHistory349
{
    internal sealed record BodyState(McObjectId[] Contents,McObjectId Last,JsonElement Nodes);
    internal sealed record Plan(HashSet<McObjectId> Detached,Dictionary<McObjectId,BodyState> Bodies);
    static string? Field(View view,string name)=>view.Metadata.GetProperty(name).ValueKind==JsonValueKind.Null?
        null:view.Metadata.GetProperty(name).GetString();
    static bool Equal(string? first,string? second)=>string.Equals(first,second,StringComparison.Ordinal);
    static void Check(bool ok,string message)=>Need(ok,message,"DRAWING_VIEW_UPDATE_MISMATCH");

    internal static Plan Observe(Snapshot before,Snapshot after,View[] selected,McObjectId[] sources,Action cancel)
    {
        var selectedIds=selected.Select(view=>view.Id).ToHashSet();
        var sourceKeys=sources.Select(id=>id.ToString()).ToHashSet(StringComparer.Ordinal);
        var selectedByKey=before.Views.Where(view=>selectedIds.Contains(view.Id)).ToDictionary(view=>view.Id.ToString(),StringComparer.Ordinal);
        var detached=new HashSet<McObjectId>();
        foreach(var old in before.Views.Where(view=>selectedIds.Contains(view.Id)))
        {
            cancel();var current=after.Views.SingleOrDefault(view=>view.Id==old.Id);
            Check(current!=null,"A selected projection has no unique retained representation.");
            string? host=Field(old,"host_solid"),previous=Field(old,"previous");
            if(Equal(host,Field(current!,"host_solid"))&&Equal(previous,Field(current!,"previous")))continue;
            var oldSources=old.Metadata.GetProperty("sources").EnumerateArray().Select(id=>id.GetString()).ToArray();
            Check(host!=null&&sources.Length>1&&sourceKeys.Contains(host)&&oldSources.Length==1&&Equal(oldSources[0],host)&&
                Field(current!,"host_solid")==null&&Field(current!,"previous")==null,
                "Only a verified single-host to retained-host multi-source projection may release its host and assigned predecessor together.");
            var formerHost=before.Bodies.SingleOrDefault(body=>body.Model&&Equal(body.Id.ToString(),host));
            Check(formerHost!=null&&formerHost.Contents.Contains(old.Id),"The former host does not contain the selected projection in its captured native history.");
            bool parentInGroup=previous!=null&&selectedByKey.TryGetValue(previous,out var parent)&&Equal(Field(parent,"host_solid"),host);
            Check(Equal(previous,host)||parentInGroup,"The released view predecessor is outside its explicitly selected ordinary host group.");
            detached.Add(old.Id);
        }
        // A partially detached selected host group is not a verified native
        // transition. Unselected groups remain protected by the writer.
        foreach(var old in before.Views.Where(view=>detached.Contains(view.Id)))
        {
            cancel();string? host=Field(old,"host_solid");
            Check(before.Views.Where(view=>selectedIds.Contains(view.Id)&&Equal(Field(view,"host_solid"),host)).All(view=>detached.Contains(view.Id)),
                "Selected projections on one former host did not complete the same multi-source history transition.");
        }
        var bodies=new Dictionary<McObjectId,BodyState>();
        foreach(var old in before.Bodies)
        {
            cancel();var removed=old.Contents.Where(detached.Contains).ToHashSet();if(removed.Count==0)continue;
            var current=after.Bodies.SingleOrDefault(body=>body.Entity==old.Entity&&body.Id==old.Id);
            Check(current!=null,"The former source host lost its unique retained body identity.");
            var wanted=old.Contents.Where(id=>!removed.Contains(id)).ToArray();
            var last=Skip(old.Last,removed,old.Previous);
            var nativeByKey=old.Contents.ToDictionary(id=>id.ToString(),StringComparer.Ordinal);
            var removedKeys=removed.Select(id=>id.ToString()).ToHashSet(StringComparer.Ordinal);
            var nodes=new List<JsonElement>();
            foreach(var item in old.Nodes.EnumerateArray())
            {
                cancel();string id=item.GetProperty("id").GetString()!;
                if(removedKeys.Contains(id))continue;
                var value=JsonNode.Parse(item.GetRawText())!.AsObject();
                var native=nativeByKey[id];
                if(old.Previous.TryGetValue(native,out var predecessor)&&removed.Contains(predecessor))
                {
                    var retained=Skip(predecessor,removed,old.Previous);
                    value["previous"]=retained.IsNull?null:retained.ToString();
                }
                nodes.Add(JsonSerializer.SerializeToElement(value,Wire.Json));
            }
            var expected=Wire.Element(nodes);
            Check(wanted.ToHashSet().SetEquals(current!.Contents)&&current.Last==last&&ModelParameterSnapshot.Equivalent(expected,current.Nodes),
                "Multi-source view release changed body history beyond the exact selected views and their predecessor bypass.");
            bodies.Add(old.Id,new(wanted,last,expected));
        }
        var afterMembership=after.Bodies.SelectMany(body=>body.Contents).ToHashSet();
        Check(!detached.Overlaps(afterMembership),
            "A released multi-source view still occupies a body history or acquired another host.");
        return new(detached,bodies);
    }
    internal static void ExpectedView(JsonObject expected,McObjectId id,Plan plan)
    {
        if(!plan.Detached.Contains(id))return;
        expected["host_solid"]=null;expected["previous"]=null;
    }
}
