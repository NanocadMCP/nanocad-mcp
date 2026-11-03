using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
namespace NanoCADBridge;
internal static class CurveSolidCreator
{
 internal static ObjectId[] Resolve(CurveSolidSpec s,Database db,Transaction tx,ObjectId model,Action? cancel=null){
  var check=cancel??(()=>{});var sources=new List<ObjectId>();
  foreach(var handle in s.Profiles)sources.Add(CadLoftSource409.Resolve(handle,true,db,tx,model,check).ObjectId);
  foreach(var handle in s.Guides)sources.Add(CadLoftSource409.Resolve(handle,false,db,tx,model,check).ObjectId);
  if(s.PathHandle!=null)sources.Add(CadLoftSource409.Resolve(s.PathHandle,false,db,tx,model,check).ObjectId);
  return sources.ToArray();
 }
 internal static Solid3d Create(CurveSolidSpec s,ObjectId[] ids,Transaction tx,Action? cancel=null,List<string>? warnings=null){
  var check=cancel??(()=>{});var copies=new List<Entity>();var body=new Solid3d();
  var copyWarnings=warnings??new List<string>();int warningStart=copyWarnings.Count;string stage="capture_native_sources";
  try {
   var states=new List<CadLoftSource409.State>();var budget=new ModelInventoryBudget(check);
   foreach(var id in ids){
    check();var state=CadLoftSource409.Capture((Entity)tx.GetObject(id,OpenMode.ForRead),check);
    budget.Add(new{state.Shape,state.Root,state.Display});states.Add(state);
   }
   // A clone cannot alias any source or another adopted clone, including a
   // source reused in another role. Unsafe wrappers are never adopted/disposed.
   var forbidden=states.Select(state=>state.Source.UnmanagedObject).ToHashSet();
   foreach(var state in states){
    stage="copy_detached_input";
    check();var copy=CadLoftClosedSplineCopy441.Detached(state,check);
    if(!forbidden.Add(copy.UnmanagedObject))throw new DomainException("UNSAFE_NATIVE_CLONE","Loft/sweep inputs require distinct independent detached native objects.");
    copies.Add(copy);stage="verify_detached_input";CadLoftSource409.VerifyCopy(state,copy,check,copyWarnings);
   }
   stage="verify_closed_profiles";
   foreach(var profile in copies.Take(s.Profiles.Length))if(profile is Curve c&&!c.Closed)throw new DomainException("INVALID_GEOMETRY","Solid profiles must be closed; use a closed curve or Region.");
   stage="verify_original_sources_before_kernel";
   CadLoftSource409.Verify(states,check);check();
   stage="prepare_independent_solid";
   body.RecordHistory=false;
   if(s.Kind=="sweep"){
    if(copies[^1] is not Curve path)throw new DomainException("INVALID_GEOMETRY","Sweep path must be a curve.");
    stage="build_sweep_options";
    var b=new SweepOptionsBuilder{Align=s.Align?SweepOptionsAlignOption.AlignSweepEntityToPath:SweepOptionsAlignOption.NoAlignment,BasePoint=new Point3d(s.BasePoint!.X,s.BasePoint.Y,s.BasePoint.Z),Bank=s.Bank,TwistAngle=s.Twist,ScaleFactor=s.Scale,CheckIntersections=true};
    stage="to_sweep_options";using var options=b.ToSweepOptions();check();
    stage="create_swept_solid";body.CreateSweptSolid(copies[0],path,options);stage="dispose_sweep_options";
   }else{
    stage="build_loft_options";var b=new LoftOptionsBuilder{Closed=s.Closed??false,NoTwist=false,AlignDirection=true,Simplify=false,Ruled=s.Ruled};
    stage="to_loft_options";using var options=b.ToLoftOptions();
    // Interpolation is an explicit requested geometric mode. Optional closure
    // is verified when supplied; omitted closure keeps its established default.
    stage="verify_loft_options";
    if(options.Ruled!=s.Ruled||s.Closed.HasValue&&options.Closed!=s.Closed.Value)
     throw new DomainException("NATIVE_OPTIONS_MISMATCH","Native loft options did not retain the requested closure or interpolation.");
    check();
    stage="create_lofted_solid";body.CreateLoftedSolid(copies.Take(s.Profiles.Length).ToArray(),copies.Skip(s.Profiles.Length).Take(s.Guides.Length).ToArray(),s.PathHandle==null?null:copies[^1],options);stage="dispose_loft_options";
   }
   stage="verify_original_sources_after_kernel";
   CadLoftSource409.Verify(states,check);CadLoftSource409.Warnings(states,warnings);
   stage="check_created_solid";CadSolidEditor.CheckGeometry(body,check);return body;
  }catch(Teigha.Runtime.Exception error){
   // The outer writer still decides not_started/unknown from its actual write
   // and transaction state. Preserve its existing native failure code here.
   var normalized=copyWarnings.Skip(warningStart).Where(value=>value.StartsWith("NATIVE_SPLINE_COPY_METADATA_NORMALIZED:",StringComparison.Ordinal)).ToArray();
   var observed=normalized.Take(4).Select(value=>value.Length<=4096?value:value[..4096]+"…").ToArray();
   body.Dispose();throw new DomainException("TRANSACTION_FAILED","nanoCAD rejected the write operation.",false,
    new{stage,api_status=error.ErrorStatus.ToString(),curve_solid_kind=s.Kind,
     copy_normalization_warnings=observed,copy_normalization_warnings_omitted=Math.Max(0,normalized.Length-observed.Length)});
  }catch{body.Dispose();throw;}finally{foreach(var e in copies)e.Dispose();}
 }
 internal static Helix Create(HelixSpec s){
  var h=new Helix();try{var o=new Point3d(s.Origin.X,s.Origin.Y,s.Origin.Z);var a=new Vector3d(s.Axis.X,s.Axis.Y,s.Axis.Z).GetNormal();var r=new Vector3d(s.Radial.X,s.Radial.Y,s.Radial.Z).GetNormal();
   h.SetAxisPoint(o,false);h.AxisVector=a;h.StartPoint=o+r*s.BaseRadius;h.BaseRadius=s.BaseRadius;h.TopRadius=s.TopRadius;h.Constrain=ConstrainType.Turns;h.Turns=s.Turns;h.Height=s.Height;h.Twist=s.RightHanded;h.CreateHelix();
   double actualHeight=(h.EndPoint-h.StartPoint).DotProduct(a);
   if(Math.Abs(actualHeight-s.Height)>Math.Max(1e-7,Math.Abs(s.Height)*1e-8)||Math.Abs(h.Turns-s.Turns)>1e-8)throw new DomainException("NATIVE_GEOMETRY_MISMATCH","Helix did not retain the requested height and turn count.");return h;
  }catch{h.Dispose();throw;}
 }
}
