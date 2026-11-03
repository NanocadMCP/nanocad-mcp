using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Identity/ownership proof is shared by the literal editor and the expression
// graph. The caller, not this proof, decides whether a formula may be changed.
internal static class NativeSketchDimensionBinding411
{
    internal sealed record Binding(string Kind,IConstraintObjectInfo[] References);
    static void Need(bool ok,string code,string message)=>NativeSketchMembers385.Need(ok,code,message);
    internal static Binding Prove(Constraints2D manager,McObjectId relation,ConstraintInfo info,
        NativeSketchMembers385.Snapshot inputs,Action cancel)
    {
        Need(info.IsDimensional,"OUT_OF_SCOPE","The selected relation is not dimensional.");
        string kind=info.Type switch{ConstraintType.DimRadial=>"radius",ConstraintType.DimDiametrical=>"diameter",ConstraintType.DimAligned=>"distance",ConstraintType.DimHorizontal=>"distance_x",ConstraintType.DimVertical=>"distance_y",ConstraintType.DimAngular=>"angle",_=>throw new DomainException("OUT_OF_SCOPE","This dimensional type has no supported control adapter.")};
        var references=new[]{info.FirstObject,info.SecondObject,info.ThirdObject}.Where(r=>r!=null&&!r.ObjectId.IsNull).Select(r=>r!).ToArray();
        Need(references.Length>0,"SKETCH_BINDING_UNAVAILABLE","The dimensional relation has no native member binding.");
        foreach(var reference in references)
        {cancel();Need(inputs.Members.Any(m=>m.ObjectId==reference.ObjectId&&(m.Segment==null||m.Segment==reference.PolylineSegmentIndex)||m.Id==reference.ObjectId),"SKETCH_BINDING_UNAVAILABLE","An actual dimensional member/segment is outside the current native input inventory.");}
        Need(references.Any(reference=>inputs.Members.Any(m=>m.Owned&&(m.Id==reference.ObjectId||m.ObjectId==reference.ObjectId&&(m.Segment==null||m.Segment==reference.PolylineSegmentIndex)))),
            "OUT_OF_SCOPE","An editable native control must bind at least one owned internal input; source-only projection controls cannot move immutable references.");
        var reverse=manager.GetAllObjectsToConstraints();Need(reverse!=null,"CONSTRAINTS_UNAVAILABLE","Actual solver member/relation reverse inventory is unavailable.");
        foreach(var reference in references)
        {cancel();Need(reverse!.Any(pair=>pair.Key==reference.ObjectId&&pair.Value!=null&&pair.Value.Contains(relation)||inputs.Members.Any(m=>m.Id==pair.Key&&m.ObjectId==reference.ObjectId&&m.Segment==reference.PolylineSegmentIndex)&&pair.Value!=null&&pair.Value.Contains(relation)),"SKETCH_BINDING_UNAVAILABLE","The actual solver reverse map does not bind this exact member/segment to the selected relation.");}
        return new(kind,references);
    }
    internal static SketchConstraintSpec Spec(ConstraintInfo info,string kind,NativeSketchMembers385.Snapshot inputs,double value)
    {
        NativeSketchMembers385.Member Member(IConstraintObjectInfo? reference)
        {
            Need(reference!=null&&!reference.ObjectId.IsNull,"SKETCH_BINDING_UNAVAILABLE","The actual dimensional operand is unavailable.");
            var matches=inputs.Members.Where(m=>m.Id==reference!.ObjectId||m.ObjectId==reference!.ObjectId&&m.Segment==reference.PolylineSegmentIndex).ToArray();
            Need(matches.Length==1,"SKETCH_BINDING_UNAVAILABLE","The actual control reference is not one unique native member/segment.");return matches[0];
        }
        string Point(IConstraintObjectInfo? reference,NativeSketchMembers385.Member member)
        {
            Need(reference!=null,"SKETCH_BINDING_UNAVAILABLE","The dimensional point is unavailable.");var role=NativeSketchMembers385.PointRole(inputs,member,reference!);
            Need(role!=null,"OUT_OF_SCOPE","This control needs an actual endpoint/center selector or an exact finite WCS pick.");return role!;
        }
        var first=Member(info.FirstObject);
        if(kind is "radius" or "diameter")
        {
            Need(first.Curve.Kind is "circle" or "arc"&&(info.SecondObject==null||info.SecondObject.ObjectId.IsNull)&&(info.ThirdObject==null||info.ThirdObject.ObjectId.IsNull),"OUT_OF_SCOPE","A radial control requires its exact single native circular operand.");
            return new(kind,first.Id.ToString(),null,null,null,value);
        }
        var second=Member(info.SecondObject);Need(info.ThirdObject==null||info.ThirdObject.ObjectId.IsNull,"OUT_OF_SCOPE","This linear control does not support a third operand.");
        string a=Point(info.FirstObject,first),b=Point(info.SecondObject,second);
        if(kind=="distance"&&first.Curve.Kind=="line"&&first.Id==second.Id&&a=="start"&&b=="end")return new("length",first.Id.ToString(),null,null,null,value);
        Need(first.Id!=second.Id||a!=b,"OUT_OF_SCOPE","A linear control requires distinct actual point references.");
        return new(kind,first.Id.ToString(),second.Id.ToString(),a,b,value);
    }
}
