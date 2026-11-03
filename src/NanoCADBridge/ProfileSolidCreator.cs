using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
namespace NanoCADBridge;
internal static class ProfileSolidCreator
{
 internal static ObjectId Resolve(ProfileSolidSpec s,Database db,Transaction tx,ObjectId model){
  var id=CadStructureWriter.Find(db,s.ProfileHandle);
  if(tx.GetObject(id,OpenMode.ForRead) is not Entity e||e.OwnerId!=model||e is not Curve and not Region)throw new DomainException("INVALID_GEOMETRY","Profile must be a closed native curve or Region in active Model Space.");return id;
 }
 internal static Solid3d Create(ProfileSolidSpec s,ObjectId id,Transaction tx){
  using var profile=(Entity)tx.GetObject(id,OpenMode.ForRead).Clone();
  if(profile is Curve c&&!c.Closed)throw new DomainException("INVALID_GEOMETRY","A solid requires a closed profile.");
  var body=new Solid3d();try{
   body.RecordHistory=false;var v=new Vector3d(s.Vector.X,s.Vector.Y,s.Vector.Z);
   if(s.Kind=="extrusion"){
    var builder=new SweepOptionsBuilder{DraftAngle=s.Angle};using var options=builder.ToSweepOptions();body.CreateExtrudedSolid(profile,v,options);
   }else{using var options=new RevolveOptions();body.CreateRevolvedSolid(profile,new Point3d(s.Origin.X,s.Origin.Y,s.Origin.Z),v.GetNormal(),s.Angle,s.StartAngle,options);}
   CadSolidEditor.CheckGeometry(body,()=>{});return body;
  }catch{body.Dispose();throw;}
 }
}
