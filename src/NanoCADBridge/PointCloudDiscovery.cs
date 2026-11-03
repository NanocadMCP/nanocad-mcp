using System.Runtime.CompilerServices;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class PointCloudDiscovery
{
    public static bool IsInsertion(ObjectId id) => !id.IsNull && id.IsValid && !id.IsErased &&
        PointCloudRuntime.TryEnsureLoaded() && NativeIsInsertion(id);

    // Failed optional runtime resolution leaves an ordinary query entity as Unknown.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool NativeIsInsertion(ObjectId id) =>
        HostMgd.PointClouds.PointCloudBridgeMdg.isPointCloudInsertion(id.OldIdPtr);
}
