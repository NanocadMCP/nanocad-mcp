using System.Globalization;
using Multicad.DatabaseServices.Constraints;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
namespace NanoCADBridge;
internal static class ModelSweepFactory
{
 internal static McObjectId[] ResolvePaths(string[] handles,DocumentState state,Transaction tx){
  return handles.Select(h=>{var id=CadStructureWriter.Find(state.Document.Database,h);var e=tx.GetObject(id,OpenMode.ForRead);
   if(e is not Curve c||c.OwnerId!=state.Document.Database.CurrentSpaceId)throw new DomainException("INVALID_GEOMETRY","Sweep path must contain top-level native curves in active Model Space.");
   var native=McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID);if(native.IsNull)throw new DomainException("OBJECT_IDENTITY_MISMATCH","Path has no native identity.");return native;
  }).ToArray();
 }
 internal static (PlanarSketch Sketch,ExtrudeAlongPathFeature Feature,SketchProfile Profile) Append(Mc3dSolid body,ModelSweepSpec s,DocumentState state,Action cancel,Action<string> trace,Action<PlanarSketch,SketchProfile?>? observeConstruction=null){
  McObjectId[] paths;using(var tx=state.Document.Database.TransactionManager.StartTransaction())paths=ResolvePaths(s.PathHandles,state,tx);
  var (sketch,profile)=ModelExtrusionFactory.CreateProfile(body,s.Section,cancel,trace,observeConstruction:observeConstruction);
  var f=body.AddExtrudeAlongPathFeature(profile.ID,paths.Select(p=>new McGeomParam{ID=p}).ToList());
  if(f==null)throw new DomainException("MODEL_UPDATE_FAILED","Native sweep feature was not created.");
  f.Name=s.Section.Name+" Sweep";f.ProfileOrientation=s.Orientation=="normal_to_path"?SweepProfileOrientation.NormalToPath:SweepProfileOrientation.ParallelToOriginalProfile;SetTaper(f,s.TaperAngle,true);
  trace("sweep_created");return(sketch,f,profile);
 }
 internal static void ValidateTaper(ExtrudeAlongPathFeature f,double radians,bool replaceFormula=false)=>
  ModelNumericDriverEditor.Validate(f,f.TaperAngleParamVarID,radians*180/Math.PI,replaceFormula);
 internal static void SetTaper(ExtrudeAlongPathFeature f,double radians,bool replaceFormula=false){
  // The initialized MCS expression is degrees; the feature getter is radians.
  f.DbEntity.Update();
  ModelNumericDriverEditor.Set(f,f.TaperAngleParamVarID,radians*180/Math.PI,replaceFormula);
 }
 internal static void Verify(ExtrudeAlongPathFeature f,ModelSweepSpec s){
  if(f.Status.ToString()!="Ok"||!f.GetPath().Select(p=>p.ID).SequenceEqual(s.PathHandles.Select(h=>McObjectId.FromHandle(unchecked((long)Convert.ToUInt64(h,16)),McDocument.ActiveDocument.ID)))||Math.Abs(f.Angle-s.TaperAngle)>1e-8||f.ProfileOrientation!=(s.Orientation=="normal_to_path"?SweepProfileOrientation.NormalToPath:SweepProfileOrientation.ParallelToOriginalProfile))throw new DomainException("MODEL_UPDATE_FAILED","Native sweep did not retain the requested path/orientation/taper or failed to rebuild.");
 }
}
