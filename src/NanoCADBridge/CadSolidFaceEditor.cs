using System.Globalization;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
namespace NanoCADBridge;
internal static class CadSolidFaceEditor
{
 internal static SubentityId[] Validate(SolidFaceEditSpec s,Solid3d body,EntityRef reference,Action cancel)
 {
  cancel();var snapshot=Wire.Element(SolidTopologyReader.Capture(body,reference,CancellationToken.None,cancel));
  if(snapshot.GetProperty("snapshot_token").GetString()!=s.TopologyToken)throw new DomainException("STALE_TOPOLOGY","Read the changed body topology before selecting faces.");
  var valid=snapshot.GetProperty("faces").EnumerateArray().Select(f=>f.GetProperty("native_id").GetString()).ToHashSet();
  if(!s.Faces.All(valid.Contains))throw new DomainException("SUBENTITY_NOT_FOUND","Face does not belong to this body snapshot.");
  return s.Faces.Select(f=>new SubentityId(SubentityType.Face,new IntPtr(long.Parse(f,CultureInfo.InvariantCulture)))).ToArray();
 }
 internal static void Apply(SolidFaceEditSpec s,Solid3d body,EntityRef reference,Action cancel,Curve? path=null)
 {
  var faces=Validate(s,body,reference,cancel);
  switch(s.Kind){
   case "offset_faces":case "presspull_faces":body.OffsetFaces(faces,s.Distance);break;
   case "extrude_faces":body.ExtrudeFaces(faces,s.Distance,s.Angle);break;
   case "extrude_faces_along_path":body.ExtrudeFacesAlongPath(faces,path??throw new DomainException("INVALID_ARGUMENT","A current native path is required."));break;
   case "remove_faces":body.RemoveFaces(faces);break;
   case "transform_faces":body.TransformFaces(faces,CadSolidEditor.Matrix(s.Transform!));break;
   case "taper_faces":body.TaperFaces(faces,new Point3d(s.Origin!.X,s.Origin.Y,s.Origin.Z),new Vector3d(s.Direction!.X,s.Direction.Y,s.Direction.Z).GetNormal(),s.Angle);break;
   default:throw new DomainException("INVALID_ARGUMENT","Unknown face edit.");
  }
  CadSolidEditor.CheckGeometry(body,cancel);
 }
}
