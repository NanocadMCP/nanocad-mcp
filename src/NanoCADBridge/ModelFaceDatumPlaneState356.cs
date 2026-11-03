using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;
using Db=Teigha.DatabaseServices;
namespace NanoCADBridge;

internal static class ModelFaceDatumPlaneState356
{
    internal sealed record Source(McObjectId Face,McObjectId Edge,G.Point3d FacePoint,G.Point3d EdgePoint,
        JsonElement FaceGeometry,JsonElement EdgeGeometry,double[] Normal,double[] Start,double[] End,string Token);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value,string code,string message)=>ModelDatumPlaneState353.Need(value,code,message);
    static string Key(McObjectId id)=>id.ToString();
    static double[] P(G.Point3d point)=>[point.X,point.Y,point.Z];
    static double[] V(G.Vector3d vector)=>[vector.X,vector.Y,vector.Z];
    static double Dot(double[] a,double[] b)=>a.Zip(b).Sum(pair=>pair.First*pair.Second);
    static double[] Sub(double[] a,double[] b)=>a.Zip(b).Select(pair=>pair.First-pair.Second).ToArray();
    internal static Source SourceGeometry(Mc3dSolid body,McObjectId face,McObjectId edge,Action cancel)
    {
        cancel();Need(!face.IsNull&&face.IsFace&&face.ParentId==body.ID&&!edge.IsNull&&edge.IsEdge&&edge.ParentId==body.ID,"DATUM_SOURCE_SCOPE_MISMATCH","Use real face/edge references of this selected part.");
        var linked=Service.GetLinkedFEVsToObject(face,G.EntityGeomType.kLine);Need(linked!=null&&linked.Contains(edge),"DATUM_SOURCE_SCOPE_MISMATCH","The straight datum edge must actually lie on the selected native planar face.");
        ModelInventoryBudget.Ids(linked,cancel);
        using var faceGeometry=Service.GetFEVGeometry(face,FEVGeometryQueryMode.ExactGeometry|FEVGeometryQueryMode.CacheLock);using var edgeGeometry=Service.GetFEVGeometry(edge,FEVGeometryQueryMode.ExactGeometry|FEVGeometryQueryMode.CacheLock);
        Need(faceGeometry!=null&&faceGeometry.GeometryType==G.EntityGeomType.kPlaneSegment&&edgeGeometry!=null&&edgeGeometry.GeometryType==G.EntityGeomType.kLine,
            "UNSUPPORTED_FACE_TYPE","The source definition requires a native planar face and straight line edge.");
        using var plane=faceGeometry.Plane;using var line=edgeGeometry.LineSeg;
        Need(plane!=null&&line!=null,"DATUM_SOURCE_GEOMETRY_UNAVAILABLE","Actual native face/line geometry is unavailable.");
        var origin=P(plane.PointOnPlane);var normal=V(plane.Normal);var start=P(line.StartPoint);var end=P(line.EndPoint);var middle=line.MidPoint;double length=line.Length;
        Need(origin.Concat(normal).Concat(start).Concat(end).All(double.IsFinite)&&double.IsFinite(length)&&length>1e-9&&Math.Abs(Dot(normal,normal)-1)<1e-8,
            "INVALID_GEOMETRY","Actual datum source geometry is nonfinite, degenerate or has no unit plane normal.");
        Need(Math.Abs(Dot(Sub(start,origin),normal))<=1e-7&&Math.Abs(Dot(Sub(end,origin),normal))<=1e-7,"INVALID_GEOMETRY","The native edge does not lie in its selected face plane.");
        var observedFace=Wire.Element(new{origin_wcs=origin,normal_wcs=normal});var observedEdge=Wire.Element(new{start_wcs=start,end_wcs=end,length});
        var stored=Wire.Element(new{body=Key(body.ID),owner=Key(body.DbEntity.Document.ID),face=Key(face),edge=Key(edge),observedFace,observedEdge});new ModelInventoryBudget(cancel).Add(stored);
        return new(face,edge,plane.PointOnPlane,middle,observedFace,observedEdge,normal,start,end,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(stored,Wire.Json))));
    }
    internal static Source Select(ModelDatumPlaneState353.Part part,ModelFaceDatumPlaneCreate356 edit,DocumentState state,string bridge,Action cancel)
    {
        var faces=Wire.Element(ModelFaceReader.Capture(part.Representation,state,bridge,CancellationToken.None,cancel));var edges=ModelEdgeReader.Capture(part.Representation,state,bridge,CancellationToken.None,cancel);
        Need(faces.GetProperty("face_token").GetString()==edit.FaceToken&&edges.edge_token==edit.EdgeToken,"STALE_TOPOLOGY","Refresh both actual native face and edge inventories before defining the datum.");
        var faceRows=faces.GetProperty("faces").EnumerateArray().Where(value=>value.GetProperty("model_face_id").GetString()!.Equals(edit.FaceId,StringComparison.OrdinalIgnoreCase)).ToArray();
        var edgeRows=edges.edges.Where(value=>value.model_edge_id.Equals(edit.EdgeId,StringComparison.OrdinalIgnoreCase)).ToArray();
        Need(faceRows.Length==1&&edgeRows.Length==1,"SUBENTITY_NOT_FOUND","The requested face/edge is absent from this current part.");
        var face=McObjectId.FromGsMarker(part.Body.ID,long.Parse(faceRows[0].GetProperty("marker").GetString()!,CultureInfo.InvariantCulture));
        var edge=McObjectId.FromGsMarker(part.Body.ID,long.Parse(edgeRows[0].marker,CultureInfo.InvariantCulture));return SourceGeometry(part.Body,face,edge,cancel);
    }
    internal static ModelFaceDatumPlaneRelationRead356? Relation(WorkPlane plane,Mc3dSolid body,Action cancel)
    {
        if(plane.DefinitionType!=WPLDefinitionType.PlaneLineNAngle)return null;
        cancel();var bindings=plane.GetDefinitionParams();Need(bindings!=null,"DATUM_DEFINITION_UNAVAILABLE","Actual native face-angle definition bindings are unavailable.");var values=bindings.ToArray();
        // Other-document/part constructions retain the existing generic datum
        // read. This typed relationship never silently invents source affinity.
        if(values.Length!=3||values[0]==null||values[1]==null||!values[0].ID.IsFace||!values[1].ID.IsEdge||values[0].ID.ParentId!=body.ID||values[1].ID.ParentId!=body.ID)return null;
        var source=SourceGeometry(body,values[0].ID,values[1].ID,cancel);using var position=plane.Position;
        Need(position!=null,"DATUM_DEFINITION_UNAVAILABLE","Actual native face-angle plane is unavailable.");var normal=V(plane.Normal);var point=P(position.Origin);
        Need(normal.Concat(point).All(double.IsFinite)&&Math.Abs(Dot(normal,normal)-1)<1e-8,"INVALID_GEOMETRY","Actual native datum has no finite unit normal.");
        Need(Math.Abs(Dot(Sub(source.Start,point),normal))<=1e-7&&Math.Abs(Dot(Sub(source.End,point),normal))<=1e-7,"NATIVE_DATUM_MISMATCH","The associated datum no longer contains its actual defining edge.");
        double dihedral=Math.Acos(Math.Clamp(Math.Abs(Dot(normal,source.Normal)),0,1));double scalar=plane.ParamVal;
        return new("face_angle",Key(body.ID),Key(source.Face),Key(source.Edge),source.FaceGeometry,source.EdgeGeometry,source.Token,dihedral,double.IsFinite(scalar)?scalar:null,
            "native_signed_input_radians_follow_source_frame; current_ParamVal_may_be_rebased; actual_oriented_plane_and_unsigned_dihedral_observed; no_WCS_rotation_or_durable_generated_edge_identity_claim");
    }
    internal static void MirrorScope(ModelDatumPlaneState353.Snapshot datum,Mc3dSolid body)
    {
        if(datum.Plane.DefinitionType!=WPLDefinitionType.PlaneLineNAngle)return;
        var parameters=datum.Plane.GetDefinitionParams();Need(parameters!=null,"DATUM_DEFINITION_UNAVAILABLE","Actual linked datum definition is unavailable.");var values=parameters.ToArray();
        if(values.Length>0&&values[0]!=null&&values[0].ID.IsFace)
            Need(datum.Read.source_relationship!=null&&datum.Read.source_relationship.source_body_id==Key(body.ID),"DATUM_SOURCE_SCOPE_MISMATCH","A face-angle mirror datum must retain observable face/lying-edge sources of this same part; foreign sources are not reassigned.");
    }
    internal static void VerifyCreated(ModelDatumPlaneState353.Part before,ModelDatumPlaneState353.Part after,ModelDatumPlaneState353.Snapshot datum,
        Source source,ModelFaceDatumPlaneCreate356 edit,Action cancel)
    {
        Need(datum.Plane.DefinitionType==WPLDefinitionType.PlaneLineNAngle&&datum.Read.name==edit.Name&&datum.Read.status=="Ok"&&!datum.Read.suppressed,
            "NATIVE_DATUM_MISMATCH","The new native datum's actual name/definition/health differs from the request.");
        var relation=datum.Read.source_relationship;Need(relation!=null&&relation.face_id==Key(source.Face)&&relation.edge_id==Key(source.Edge)&&relation.source_token==source.Token,
            "DATUM_DEFINITION_CHANGED","Native construction did not retain the requested same-part face/edge sources and their actual geometry.");
        double requestedDihedral=Math.Acos(Math.Clamp(Math.Abs(Math.Cos(edit.NativeAngleRadians)),0,1));
        Need(ModelDatumPlaneState353.Requested(relation.unsigned_dihedral_radians,requestedDihedral),"NATIVE_DATUM_MISMATCH","The native datum's physical unsigned dihedral differs from its requested native angular relation.");
        Need(before.DrawingDocument==after.DrawingDocument&&before.OwnerDocument==after.OwnerDocument&&before.Representation==after.Representation&&before.Body.ID==after.Body.ID,
            "OBJECT_IDENTITY_MISMATCH","Creating native source construction changed its current part/document identity.");
        Need(before.Contents.All(after.Contents.Contains)&&after.Contents.Except(before.Contents).All(id=>id==datum.Plane.ID)&&after.Body.LastPartItemID==before.Views.LastHistoryItem,
            "MODEL_STATE_UNAVAILABLE","Creating a native datum changed unrelated retained history/terminal.");ModelDatumPlaneState353.MirrorAffinity(datum,after.Body);
        var oldVariables=before.Parameters.GetProperty("variables").EnumerateArray().ToDictionary(value=>value.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var newVariables=after.Parameters.GetProperty("variables").EnumerateArray().ToDictionary(value=>value.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        string? added=datum.Read.parameter_variable_id;
        Need(oldVariables.Keys.All(newVariables.ContainsKey)&&newVariables.Keys.Except(oldVariables.Keys,StringComparer.OrdinalIgnoreCase).All(id=>id==added),
            "MODEL_DRIVER_CHANGED","New angular construction lost a retained variable or allocated an unclassified native driver.");
        if(added!=null&&!oldVariables.ContainsKey(added))Need(newVariables[added].GetProperty("host_object_id").GetString()==Key(datum.Plane.ID)&&datum.Read.driver!=null,
            "MODEL_DRIVER_CHANGED","The new native angular driver lacks exact plane ownership.");
        foreach(var prior in oldVariables)
        {
            cancel();var old=JsonNode.Parse(prior.Value.GetRawText())!.AsObject();var current=JsonNode.Parse(newVariables[prior.Key].GetRawText())!.AsObject();
            foreach(string key in new[]{"downstream_variables","downstream_objects"})
            {
                var a=old[key]!.AsArray().Select(item=>item!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);var b=current[key]!.AsArray().Select(item=>item!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Need(a.IsSubsetOf(b)&&b.Except(a,StringComparer.OrdinalIgnoreCase).All(id=>key=="downstream_variables"?id==added:id==Key(datum.Plane.ID)),"MODEL_DEPENDENCY_CHANGED","Creating native construction changed an unrelated variable dependency.");
                old.Remove(key);current.Remove(key);
            }
            Need(Wire.Element(old).GetRawText()==Wire.Element(current).GetRawText(),"MODEL_DRIVER_CHANGED","Creating native construction changed a retained variable expression/value/metadata.");
        }
        var aParameters=JsonNode.Parse(before.Parameters.GetRawText())!.AsObject();var bParameters=JsonNode.Parse(after.Parameters.GetRawText())!.AsObject();aParameters.Remove("handles");bParameters.Remove("handles");aParameters.Remove("variables");bParameters.Remove("variables");
        if(bParameters["nodes"] is JsonArray nodes)for(int i=nodes.Count-1;i>=0;i--){cancel();if(nodes[i]!["mc_id"]!.GetValue<string>().Equals(Key(datum.Plane.ID),StringComparison.OrdinalIgnoreCase))nodes.RemoveAt(i);}
        Need(ModelParameterSnapshot.Equivalent(Wire.Element(aParameters),Wire.Element(bParameters))&&before.Metadata.GetRawText()==after.Metadata.GetRawText()&&before.Display.GetRawText()==after.Display.GetRawText(),
            "MODEL_STATE_UNAVAILABLE","Creating native source construction changed retained shape/history/design/source/readable metadata.");
        Need(before.Inventory.All(after.Inventory.Contains)&&after.Inventory.Except(before.Inventory).All(id=>datum.Representation==id),"MODEL_STATE_UNAVAILABLE","Native datum creation changed unrelated drawing membership.");
        Need(datum.Read.direct_consumer_ids.Length==0&&datum.Read.downstream_consumer_ids.Length==0,"DATUM_DEPENDENCY_CHANGED","The newly created plane acquired an unrequested consumer.");ModelViewFollowers.Verify(after.Body,before.Views,cancel);
    }
}
