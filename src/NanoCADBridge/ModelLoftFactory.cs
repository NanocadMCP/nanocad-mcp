using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
namespace NanoCADBridge;
internal static class ModelLoftFactory
{
 internal static McObjectId[][] ResolvePaths(ModelLoftSpec s,DocumentState state,Transaction tx)=>s.GuideHandles.Concat(s.PathHandle==null?Array.Empty<string[]>():new[]{new[]{s.PathHandle}}).Select(p=>ModelSweepFactory.ResolvePaths(p,state,tx)).ToArray();
 internal static (PlanarSketch Sketch,LoftFeature Feature,SketchProfile Profile) Append(Mc3dSolid body,ModelLoftSpec s,DocumentState state,Action cancel,Action<string> trace,Action<PlanarSketch,SketchProfile?>? observeConstruction=null){
  McObjectId[][] paths;using(var tx=state.Document.Database.TransactionManager.StartTransaction())paths=ResolvePaths(s,state,tx);
  var profiles=s.Sections.Select(section=>ModelExtrusionFactory.CreateProfile(body,section,cancel,trace,observeConstruction:observeConstruction)).ToArray();
  var f=body.AddLoftFeature(profiles[0].Profile.ID,profiles.Skip(1).Select(p=>p.Profile.ID).ToArray());
  if(f==null)throw new DomainException("MODEL_UPDATE_FAILED","Native loft was not created.");
  f.Name="Loft";f.Closed=false;f.FirstSectionCondition=LoftCondition.Free;f.LastSectionCondition=LoftCondition.Free;
  if(s.PathHandle!=null){f.LoftType=LoftType.WithCenterLine;f.Path=new McGeomParam{ID=paths[0][0]};}
  else if(paths.Length>0){f.LoftType=LoftType.WithRails;foreach(var rail in paths)if(!f.AddGuideRailsParams(rail.Select(id=>new McGeomParam{ID=id}).ToList()))throw new DomainException("MODEL_UPDATE_FAILED","Native loft rejected a guide rail.");}
  else f.LoftType=LoftType.Regular;
  trace("loft_created");return(profiles[0].Sketch,f,profiles[0].Profile);
 }
 internal static void Verify(LoftFeature f,ModelLoftSpec s){
  if(f.Status.ToString()!="Ok"||f.ProfileID.IsNull||f.Closed||f.SectionsCount!=s.Sections.Length)throw new DomainException("MODEL_UPDATE_FAILED","Native loft failed to rebuild or lost its profile.");
  var expected=s.PathHandle!=null?LoftType.WithCenterLine:s.GuideHandles.Length>0?LoftType.WithRails:LoftType.Regular;
  if(f.LoftType!=expected||f.GuideRailsCount!=s.GuideHandles.Length)throw new DomainException("MODEL_UPDATE_FAILED","Loft path/guide mode differs from requested mode.");
 }
}
