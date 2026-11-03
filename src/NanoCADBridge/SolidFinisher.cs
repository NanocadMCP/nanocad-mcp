using System.Globalization;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class SolidFinisher
{
    private static JsonElement Snapshot(Solid3d solid,EntityRef source,Action? cancel)=>Wire.Element(SolidTopologyReader.Capture(solid,source,CancellationToken.None,cancel));
    private static void Need(bool condition,string code,string message){if(!condition)throw new DomainException(code,message);}
    internal static void Validate(SolidFinishingCopySpec spec,Solid3d source,EntityRef reference,Action? cancel=null)
    {
        var snapshot=Snapshot(source,reference,cancel);
        Need(snapshot.GetProperty("snapshot_token").GetString()==spec.TopologyToken,"STALE_TOPOLOGY","Reacquire the source topology; snapshot differs.");
        string collection=spec.Kind=="shell_copy"?"faces":"edges";
        var ids=snapshot.GetProperty(collection).EnumerateArray().Select(x=>x.GetProperty("native_id").GetString()).ToHashSet(StringComparer.Ordinal);
        Need(spec.Subentities.All(ids.Contains),"SUBENTITY_NOT_FOUND","Selected subentity does not belong to this source snapshot.");
        if(spec.BaseFace!=null)Need(snapshot.GetProperty("faces").EnumerateArray().Any(x=>x.GetProperty("native_id").GetString()==spec.BaseFace),"SUBENTITY_NOT_FOUND","Chamfer base face does not belong to the source.");
    }
    internal static Solid3d Create(SolidFinishingCopySpec spec,Solid3d source,EntityRef reference,Action? cancel=null)
    {
        Validate(spec,source,reference,cancel);cancel?.Invoke();
        var result=(Solid3d)source.Clone();
        try
        {
            // IDs are never assumed to survive cloning. Verify the entire
            // topology under the same logical source reference before editing.
            Need(result.ObjectId.IsNull&&Snapshot(result,reference,cancel).GetProperty("snapshot_token").GetString()==spec.TopologyToken,
                "SUBENTITY_IDENTITY_UNAVAILABLE","Clone does not retain the complete source topology mapping.");
            Apply(spec,result,reference,true,cancel);
            return result;
        }
        catch{result.Dispose();throw;}
    }
    internal static SolidStockGeometry353? Apply(SolidFinishingCopySpec spec,Solid3d target,EntityRef reference,bool topologyAlreadyValidated=false,Action? cancel=null)
    {
            if(!topologyAlreadyValidated)Validate(spec,target,reference,cancel);
            cancel?.Invoke();
            SubentityId Id(string value,SubentityType kind)=>new(kind,new IntPtr(long.Parse(value,CultureInfo.InvariantCulture)));
            var kind=spec.Kind=="shell_copy"?SubentityType.Face:SubentityType.Edge;
            var selected=spec.Subentities.Select(value=>Id(value,kind)).ToArray();
            SolidStockGeometry353? stock=null;
            if(spec.Kind=="fillet_copy")target.FilletEdges(selected,new DoubleCollection(Enumerable.Repeat(spec.Size,selected.Length).ToArray()),new DoubleCollection(new double[selected.Length]),new DoubleCollection(new double[selected.Length]));
            else if(spec.Kind=="chamfer_copy")target.ChamferEdges(selected,Id(spec.BaseFace!,SubentityType.Face),spec.Size,spec.OtherSize);
            else stock=CadSolidStockEditor353.Shell(spec,target,selected,cancel??(()=>{}));
            cancel?.Invoke();Need(!target.IsNull,"EMPTY_SOLID_RESULT","Native finishing produced an empty body.");
            double after=target.MassProperties.Volume;
            Need(double.IsFinite(after)&&after>0,"INVALID_GEOMETRY","Native finishing did not leave a finite positive body.");
            return stock;
    }
}
