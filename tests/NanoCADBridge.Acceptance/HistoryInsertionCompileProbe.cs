// Compile-only SDK probe for issue246. No CAD command invokes this method.
using Multicad.Mc3D;

namespace NanoCADBridge.Acceptance;

internal static class HistoryInsertionCompileProbe
{
    internal static bool Signatures(HistoryItem modelAnchor,HistoryItem rootView,
        HistoryItem terminalView,HistoryItem newFeature)
    {
        bool canMoveEnd=modelAnchor.SetAsLastPartItem(true);
        bool canRelink=rootView.SetOrderObjBefore(newFeature.ID,true);
        _=terminalView.IsBeyondEndOfPart;
        return canMoveEnd&&canRelink;
    }
}
