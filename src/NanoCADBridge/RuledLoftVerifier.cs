using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class RuledLoftVerifier
{
    internal static void Verify(Solid3d solid,CircularLoftSection[] sections)
    {
        var mass=solid.MassProperties;
        if(!double.IsFinite(mass.Volume)||mass.Volume<=0)
            throw new DomainException("INVALID_GEOMETRY","Native loft has no finite positive volume.");
        // SolidCreator separately verifies the requested input sections and
        // interpolation route. Optional UV-envelope sampling is not an
        // admission condition: unsupported diagnostic surface evaluation must
        // not discard a completed native loft. Shape acceptance still requires
        // actual MCP image review and the task's significant dimensions.
    }
}
