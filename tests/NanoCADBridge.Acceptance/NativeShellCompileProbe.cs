// Compile-only SDK probe. Never dispatched into a nanoCAD document.
using Multicad;
using Multicad.Geometry;
using Multicad.Mc3D;

namespace NanoCADBridge.Acceptance;

internal static class NativeShellCompileProbe
{
    internal static void Signatures(Mc3dSolid body)
    {
        var markers=body.GetSolidBody().GetFaces();
        if(markers.Length==0)return;
        var geometry=body.GetSolidBody().GetSubentGeometry(markers[0]);
        var surface=geometry?.AsSurface();
        if(surface is PlaneEnt3d plane){_=plane.Origin;_=plane.Normal;}
        var face=McObjectId.FromGsMarker(body.ID,markers[0]);
        if(!face.IsFace||face.ParentId!=body.ID)return;
        ThicknessFeature? shell=body.AddThicknessFeature(McObjectId.Null,
            FeatureExtentDirection.Negative,1,0);
        if(shell==null)return;
        shell.AddOutFaces(face);
        _=shell.OutFaces;
        _=shell.SurfaceId;
        _=shell.Direction;
    }
}
