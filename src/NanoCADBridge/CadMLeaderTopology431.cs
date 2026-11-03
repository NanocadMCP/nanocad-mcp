using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadMLeaderTopology431
{
    const string Warning="NATIVE_MLEADER_TOPOLOGY: Native leader lines/vertices were edited on the retained callout; text/style and unrelated owned data are retained. Native landing/dogleg/text placement can reflow. Inspect actual geometry.leader_editing after the group. Undo/recovery is not guaranteed.";
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool yes,string message,string code="INVALID_MLEADER_TOPOLOGY")
    {if(!yes)throw new DomainException(code,message);}
    static bool Same(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    static Point3d P(Point3 p)=>new(p.X,p.Y,p.Z);
    static bool Near(Point3d a,Point3d b)=>a.DistanceTo(b)<=Tolerance.Global.EqualPoint;
    static JsonElement Content(JsonElement content)
    {var node=JsonNode.Parse(content.GetRawText())!.AsObject();node.Remove("line_styles");return Wire.Element(node);}
    static CadMLeaderGeometry388.State Prepare(MLeader source,MLeaderTopologyEdit431 edit,Transaction tr,Action cancel)
    {
        CadMLeaderGeometry388.Ownership(source,tr,cancel);var before=CadMLeaderGeometry388.Capture(source,tr,cancel);
        Need(MLeaderGeometryValidation388.Token(before,cancel)==edit.ExpectedGeometryToken,"Leader geometry/content/layout changed; refresh its complete geometry token.","GEOMETRY_CHANGED");return before;
    }
    internal static void CheckSource(Entity source,MLeaderTopologyEdit431 edit,Transaction tr,Action cancel)
    {Need(source.GetType()==typeof(MLeader),"Select an exact native MLeader.","UNSUPPORTED_ENTITY_TYPE");_=Prepare((MLeader)source,edit,tr,cancel);}
    static void Points(MLeader source,int line,Point3d[] requested,Action cancel)
    {
        int count=source.VerticesCount(line);Need(count>=0,"Native leader returned a negative vertex count.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");Need(requested.Length>=2,"A surviving line requires at least two vertices.");
        for(int i=1;i<requested.Length;i++)Need(!Near(requested[i-1],requested[i]),"Adjacent leader vertices would collapse.","DEGENERATE_GEOMETRY");
        // Public API supplies endpoint insertion/removal, not InsertVertex. Keep
        // this actual line ID and reindex its ordinary ordered vertices in place.
        while(count<requested.Length)
        {
            cancel();Point3d seed;
            if(count>=2){var tail=source.GetVertex(line,count-1);var prior=source.GetVertex(line,count-2);seed=Near(tail,prior)?tail+Vector3d.XAxis:tail+(tail-prior);}
            else
            {
                seed=requested[count];
                if(count==1&&Near(source.GetVertex(line,0),seed))seed+=Vector3d.XAxis;
            }
            source.AddLastVertex(line,seed);int current=source.VerticesCount(line);
            Need(current>count,"Native AddLastVertex did not increase the selected line's actual vertex count.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");count=current;
        }
        // AddLeaderLine on an existing native branch can initialize its own
        // landing vertices. Observe that state instead of appending N points to
        // an assumed empty line, which can retain an incorrect initial arrow.
        while(count>requested.Length)
        {
            cancel();source.RemoveLastVertex(line);int current=source.VerticesCount(line);
            Need(current>=requested.Length&&current<count,"Native RemoveLastVertex did not retain the requested surviving line topology.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");count=current;
        }
        for(int i=0;i<requested.Length;i++){cancel();source.SetVertex(line,i,requested[i]);}
    }
    static string[] Run(MLeader source,CadMLeaderGeometry388.Geometry before,MLeaderTopologyEdit431 edit,Action cancel)
    {
        var notices=new List<string>{Warning};var removed=new HashSet<int>();var selected=new HashSet<int>();var requested=new Dictionary<int,Point3d[]>();
        foreach(var change in edit.Edits)
        {
            cancel();var lines=source.GetLeaderIndexes().Cast<int>().SelectMany(branch=>source.GetLeaderLineIndexes(branch).Cast<int>().Select(line=>(Branch:branch,Line:line))).ToArray();
            if(change.Action=="add_line")
            {
                if(change.LeaderIndex is {} existing)Need(lines.Any(l=>l.Branch==existing),"Selected leader_index does not exist.","MLEADER_BRANCH_NOT_FOUND");
                int branch=change.LeaderIndex??source.AddLeader();int line=source.AddLeaderLine(branch);selected.Add(line);
                requested[line]=change.Points.Select(P).ToArray();
                Points(source,line,requested[line],cancel);
                Need(source.VerticesCount(line)==change.Points.Length,"Added native leader line did not retain all requested vertices.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");
                for(int i=0;i<change.Points.Length;i++)Need(Near(source.GetVertex(line,i),P(change.Points[i])),"Added leader vertex differs from requested WCS point.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");
                notices.Add("NATIVE_MLEADER_LINE_ADDED: leader_index="+branch+", leader_line_index="+line+".");continue;
            }
            if(change.Action=="remove_branch")
            {
                int branch=change.LeaderIndex!.Value;var victims=lines.Where(l=>l.Branch==branch).ToArray();Need(victims.Length>0,"Selected leader_index does not exist.","MLEADER_BRANCH_NOT_FOUND");
                Need(victims.Length<lines.Length,"Removing the last callout branch would leave no leader; use ordinary erase to remove the whole callout.");
                source.RemoveLeader(branch);foreach(var victim in victims){removed.Add(victim.Line);requested.Remove(victim.Line);}
                Need(!source.GetLeaderIndexes().Cast<int>().Contains(branch),"Requested native branch deletion did not complete.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");continue;
            }
            int chosen=change.LeaderLineIndex!.Value;Need(lines.Any(l=>l.Line==chosen),"Selected leader_line_index does not exist.","MLEADER_LINE_NOT_FOUND");
            if(change.Action=="remove_line")
            {Need(lines.Length>1,"Removing the last callout line would leave no leader; use ordinary erase to remove the whole callout.");source.RemoveLeaderLine(chosen);removed.Add(chosen);requested.Remove(chosen);
                Need(!source.GetLeaderIndexes().Cast<int>().SelectMany(b=>source.GetLeaderLineIndexes(b).Cast<int>()).Contains(chosen),"Requested native line deletion did not complete.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");continue;}
            int size=source.VerticesCount(chosen),vertex=change.VertexIndex!.Value;
            var points=Enumerable.Range(0,size).Select(index=>source.GetVertex(chosen,index)).ToList();
            if(change.Action=="insert_vertex")
            {Need(vertex<=size,"vertex_index is the insertion position in the current ordered line.","MLEADER_VERTEX_NOT_FOUND");points.Insert(vertex,P(change.Points.Single()));}
            else{Need(vertex<size,"Selected vertex is absent from the current native line.","MLEADER_VERTEX_NOT_FOUND");Need(size>2,"A surviving native leader line requires at least two vertices.");points.RemoveAt(vertex);}
            selected.Add(chosen);requested[chosen]=points.ToArray();Points(source,chosen,points.ToArray(),cancel);
            Need(source.VerticesCount(chosen)==points.Count,"Native vertex count differs from the requested edit.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");
            for(int i=0;i<points.Count;i++)Need(Near(source.GetVertex(chosen,i),points[i]),"Native ordered vertices differ from the requested edit.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");
        }
        if(!source.ObjectId.IsNull)source.RecordGraphicsModified(true);var after=CadMLeaderGeometry388.CaptureGeometry(source,cancel);
        Need(MLeaderTextVerification.Unchanged(Content(before.Content),Content(after.Content))&&Same(before.Display,after.Display),"Topology editing changed retained raw text/style/display.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");
        var oldStyles=before.Content.GetProperty("line_styles").EnumerateArray().ToDictionary(s=>s.GetProperty("leader_line_index").GetInt32());
        var newStyles=after.Content.GetProperty("line_styles").EnumerateArray().ToDictionary(s=>s.GetProperty("leader_line_index").GetInt32());
        foreach(var prior in before.Lines)
        {
            cancel();if(removed.Contains(prior.leader_line_index))continue;
            var current=after.Lines.SingleOrDefault(line=>line.leader_line_index==prior.leader_line_index);
            Need(current!=null&&current.leader_index==prior.leader_index,"Topology editing removed/reparented an unselected retained line.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");
            Need(newStyles.TryGetValue(prior.leader_line_index,out var style)&&Same(oldStyles[prior.leader_line_index],style),"Native topology editing changed a retained line override.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");
            if(!selected.Contains(prior.leader_line_index)&&!Same(Wire.Element(prior.vertices_wcs),Wire.Element(current.vertices_wcs)))
                notices.Add("NATIVE_MLEADER_LAYOUT_ADJUSTMENT: Native layout adjusted unselected line "+prior.leader_line_index+"; read actual vertices and landing placement.");
        }
        // A native allocator may reuse an explicitly removed line ID for a later
        // requested add. Verify deletion at its action and final requested geometry
        // by the surviving actual ID, without inventing monotonic index semantics.
        foreach(var wanted in requested)
        {
            cancel();var line=after.Lines.SingleOrDefault(l=>l.leader_line_index==wanted.Key);
            Need(line!=null&&line.vertices_wcs.Length==wanted.Value.Length,"Final native line/vertex topology differs from the ordered edits.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");
            for(int i=0;i<wanted.Value.Length;i++)Need(Near(new(line.vertices_wcs[i][0],line.vertices_wcs[i][1],line.vertices_wcs[i][2]),wanted.Value[i]),"Final native leader vertex differs from the ordered edits.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");
        }
        if(!Same(before.Layout,after.Layout))notices.Add("NATIVE_MLEADER_LAYOUT_ADJUSTMENT: Native text/landing/dogleg layout reflowed; inspect the complete result.");
        return notices.ToArray();
    }
    static ModifiedEntities Result(ModifyArguments args,MLeaderTopologyEdit431 edit)=>new(args.OperationId,"committed",false,[new(args.BridgeId,args.DocumentId,edit.Handles.Single())],[],[],[],"not_guaranteed");
    internal static void Preview(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,Action cancel)
    {
        var edit=(MLeaderTopologyEdit431)args.Operations.Single();var source=(MLeader)tr.GetObject(resolved.Sources[edit.Handles.Single()],OpenMode.ForRead);var before=Prepare(source,edit,tr,cancel);
        using var clone=CadContourBudget344.Detached(source);var detached=CadMLeaderGeometry388.CaptureGeometry((MLeader)clone,cancel);
        Need(Same(Content(before.Geometry.Content),Content(detached.Content))&&Same(Wire.Element(before.Geometry.Lines),Wire.Element(detached.Lines)),"Detached leader preview did not retain original content/geometry.","UNSAFE_NATIVE_CLONE");
        var notices=Run((MLeader)clone,detached,edit,cancel);SerializedCapacity357.Check(new{data=Result(args,edit),warnings=notices},Wire.PayloadBudget-16384,cancel);
        Need(MLeaderGeometryValidation388.Token(before,cancel)==MLeaderGeometryValidation388.Token(CadMLeaderGeometry388.Capture(source,tr,cancel),cancel),"Detached preview changed the resident callout.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");
    }
    internal static ModifiedEntities Apply(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,WriteJob job,List<string>? warnings)
    {
        var edit=(MLeaderTopologyEdit431)args.Operations.Single();var source=(MLeader)tr.GetObject(resolved.Sources[edit.Handles.Single()],OpenMode.ForRead);
        Need(source.OwnerId==resolved.Model&&CadBlockSpace.RequireWriteSpace(source.Database,tr,args.TargetSpace,job.CheckCancellation)==resolved.Model,"Leader differs from requested active space.","OUT_OF_SCOPE");
        var before=Prepare(source,edit,tr,job.CheckCancellation);source.UpgradeOpen();var notices=Run(source,before.Geometry,edit,job.CheckCancellation);var after=CadMLeaderGeometry388.Capture(source,tr,job.CheckCancellation);
        Need(Same(before.Metadata,after.Metadata),"Native topology edit changed root/owner/owned application data.","NATIVE_MLEADER_TOPOLOGY_MISMATCH");
        var result=Result(args,edit);SerializedCapacity357.Check(new{data=result,warnings=notices},Wire.PayloadBudget-16384,job.CheckCancellation);warnings?.AddRange(notices);return result;
    }
}
