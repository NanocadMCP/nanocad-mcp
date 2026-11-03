using NanoCAD.Contracts;
using Teigha.DatabaseServices;
namespace NanoCADBridge;
internal static class RegionCreator
{
 internal static ObjectId[] Resolve(RegionSpec s,Database db,Transaction tx,ObjectId model)=>s.Boundaries.SelectMany(x=>x).Select(h=>{
  var id=CadStructureWriter.Find(db,h);if(tx.GetObject(id,OpenMode.ForRead) is not Curve c||c.OwnerId!=model)throw new DomainException("INVALID_GEOMETRY","Region boundaries require native Model curves.");return id;
 }).ToArray();
 internal static Region Create(RegionSpec s,ObjectId[] ids,Transaction tx){
  Region? result=null;int offset=0;
  try{foreach(var loop in s.Boundaries){
   var copies=new List<Entity>();Region? region=null;
   try{
    using var curves=new DBObjectCollection();foreach(var id in ids.Skip(offset).Take(loop.Length)){var c=(Entity)tx.GetObject(id,OpenMode.ForRead).Clone();copies.Add(c);curves.Add(c);}offset+=loop.Length;
    using var regions=Region.CreateFromCurves(curves);
    if(regions.Count!=1){foreach(DBObject o in regions)o.Dispose();throw new DomainException("INVALID_GEOMETRY","Each boundary must produce one closed planar region.");}
    region=(Region)regions[0];if(result==null){result=region;region=null;}else result.BooleanOperation(BooleanOperationType.BoolSubtract,region);
   }finally{region?.Dispose();foreach(var c in copies)c.Dispose();}
  }
  if(result==null||result.IsNull||!double.IsFinite(result.Area)||result.Area<=0)throw new DomainException("EMPTY_GEOMETRY","Region result is empty.");return result;
  }catch{result?.Dispose();throw;}
 }
}
