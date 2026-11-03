using Multicad.DatabaseServices;
using Multicad.Mc3D;
namespace NanoCADBridge;
internal static class ModelProfileConsumerOwnership362
{
    // Compatibility entry point for existing callers. Reuse is admitted only
    // after the full supported forward consumer graph and single-part owner
    // have been read; it is not an implicit consumer transfer.
    internal static void RequireUnused(SketchProfile selected,McDocument owner,Action cancel)=>ModelProfileConsumers410.Capture(selected,owner,cancel);
    internal static ModelProfileConsumers410.Snapshot Capture(SketchProfile selected,McDocument owner,Action cancel)=>ModelProfileConsumers410.Capture(selected,owner,cancel);
}