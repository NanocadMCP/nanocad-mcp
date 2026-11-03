using System.Globalization;
using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;
namespace NanoCADBridge;
internal static class ModelExtrusionExtentEditor
{
 internal static ExtrudeParamExtType Mode(JsonElement d)=>d.GetProperty("extent").GetString() switch{"through_all"=>ExtrudeParamExtType.All,"to_face"=>ExtrudeParamExtType.ToFace,_=>ExtrudeParamExtType.Dist};
 internal static FeatureExtentDirection Direction(JsonElement d)=>d.GetProperty("direction").GetString()=="positive"?FeatureExtentDirection.Positive:FeatureExtentDirection.Negative;
 internal static bool ReplacesFormulas(JsonElement d)=>d.TryGetProperty("replace_driver_formulas",out var r)&&r.GetBoolean();
 internal static void Validate(ExtrudeFeature f,JsonElement d){
  if(Mode(d)==ExtrudeParamExtType.Dist)ModelNumericDriverEditor.Validate(f,f.DistanceParamVarID,d.GetProperty("distance").GetDouble(),ReplacesFormulas(d));
  ModelNumericDriverEditor.Validate(f,f.TaperAngleParamVarID,d.GetProperty("taper_angle").GetDouble()*180/Math.PI,ReplacesFormulas(d));
 }
 internal static void Apply(ExtrudeFeature f,JsonElement d,McObjectId face){
  f.Direction=Direction(d);f.Extent=Mode(d);
  using(var target=face.IsNull?new McObjectComplexId():new McObjectComplexId(face))f.EntityTo1_id=target;
  f.DbEntity.Update();
  void Set(McObjectId id,double value)=>ModelNumericDriverEditor.Set(f,id,value,ReplacesFormulas(d));
  if(f.Extent==ExtrudeParamExtType.Dist)Set(f.DistanceParamVarID,d.GetProperty("distance").GetDouble());
  Set(f.TaperAngleParamVarID,d.GetProperty("taper_angle").GetDouble()*180/Math.PI);
 }
 internal static bool Verify(ExtrudeFeature f,JsonElement d){
  if(f.Extent!=Mode(d)||f.Direction!=Direction(d)||Math.Abs(f.Angle-d.GetProperty("taper_angle").GetDouble())>1e-8)return false;
  if(f.Extent==ExtrudeParamExtType.Dist&&Math.Abs(f.Distance-d.GetProperty("distance").GetDouble())>1e-7)return false;
  if(f.Extent==ExtrudeParamExtType.ToFace){using var target=f.EntityTo1_id;if(target==null||target.IsNull||!target.TargetId.ToString().Equals(d.GetProperty("face_id").GetString(),StringComparison.OrdinalIgnoreCase))return false;}
  return true;
 }
 internal static object Read(ExtrudeFeature f){
  using var target=f.EntityTo1_id;
  return new{kind="extrusion",profile_id=f.ProfileID.ToString(),extent=f.Extent.ToString(),direction=f.Direction.ToString(),distance=f.Distance,taper_angle=f.Angle,terminal_face_id=target==null||target.IsNull?null:target.TargetId.ToString(),part_operation=f.PartOperation.ToString()};
 }
}
