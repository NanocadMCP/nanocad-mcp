using System.Collections;

namespace NanoCAD.Contracts;

public static class DimensionMetadataEnumeration
{
    // Presence only: never reads Current or copies an unbounded XData payload.
    public static bool HasAny(IEnumerator enumerator, Action check)
    {
        try
        {
            check();
            bool present = enumerator.MoveNext();
            check();
            return present;
        }
        finally { (enumerator as IDisposable)?.Dispose(); }
    }
}
