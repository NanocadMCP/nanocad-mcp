// Read-only public MultiCAD parameter-role IDs omitted by mapimgd.dll.
// Called on the document thread with actual document and feature work IDs.
// Complete vendor interfaces provide normal QueryInterface/refcount lifetime.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cmath>
#include <cwchar>

// Public external-module configuration (SDK McsAddin.h).
#define _MCS_CORE_ONLY
#define NOACGE
#define MCGEL
#include "sdk-compat/generated/IMcObjects.h"
#include "sdk-compat/generated/IMcDocuments.h"
#include "sdk-compat/generated/3DCore.h"

// Exact public IMcs.h declarations. Including the complete objects/documents
// interfaces above avoids unrelated codec/filesystem headers omitted by SDK26.
extern "C"
{
    extern MCTYP_API IMcObjectsManager* gpMcObjManager;
    extern MCTYP_API IMcDocumentsManager* gpMcDocManager;
}

namespace
{
    constexpr int GuidCapacity = 39; // Braced GUID plus terminator.

    HRESULT ParseId(const wchar_t* text, mcsWorkID& id)
    {
        // Use the SDK string parser; no pointers or option bits cross the ABI.
        if (!text || wcsnlen_s(text, GuidCapacity) != GuidCapacity - 1 ||
            text[0] != L'{' || text[GuidCapacity - 2] != L'}' ||
            !id.setStrID(text) || id.isNull())
            return E_INVALIDARG;
        return S_OK;
    }

    HRESULT PrepareBuffer(wchar_t* text, int capacity)
    {
        if (!text || capacity < GuidCapacity) return E_INVALIDARG;
        text[0] = L'\0';
        return S_OK;
    }

    HRESULT WriteId(const mcsWorkID& id, wchar_t* text, int capacity)
    {
        if (id.isNull()) return S_FALSE;
        return id.getStrID(text, capacity) ? S_OK : E_FAIL;
    }

    HRESULT ReadObject(const wchar_t* documentText, const wchar_t* featureText,
                       IMcObjectPtr& object)
    {
        mcsWorkID documentId, featureId;
        HRESULT status = ParseId(documentText, documentId);
        if (FAILED(status)) return status;
        status = ParseId(featureText, featureId);
        if (FAILED(status)) return status;
        if (!gpMcObjManager || !gpMcDocManager) return E_UNEXPECTED;

        // Explicit owner; never fall back to the current/active document.
        IMcDocumentPtr document = gpMcDocManager->getDocument(documentId);
        if (!document || document->ID() != documentId)
            return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        object = gpMcObjManager->getObject(featureId);
        if (!object || object->ID() != featureId)
            return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        IMcDbEntityPtr entity = object;
        if (!entity) return E_NOINTERFACE;
        if (entity->getDocumentID() != document->ID()) return E_INVALIDARG;
        return S_OK;
    }
}

extern "C" __declspec(dllexport) std::uint32_t __cdecl
NanoCAD_NativeParametersAbiVersion()
{
    return 1;
}

// channel: 0=kPositiveExtDir (managed GetFaceThickness),
//          1=kNegativeExtDir (managed GetFaceThickness2).
// This is independent of the shell feature's overall Direction property.
// WID_NULL selects the default thickness, not an individual face.
// S_FALSE means no existing variable; no variable is created.
extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_GetShellThicknessVariable412(
    const wchar_t* documentGuid, const wchar_t* featureGuid, int channel,
    wchar_t* variableGuid, int variableCapacity, double* value)
{
    if (!value) return E_POINTER;
    *value = NAN;
    HRESULT status = PrepareBuffer(variableGuid, variableCapacity);
    if (FAILED(status)) return status;
    if (channel != 0 && channel != 1) return E_INVALIDARG;
    try
    {
        IMcObjectPtr object;
        status = ReadObject(documentGuid, featureGuid, object);
        if (FAILED(status)) return status;
        IMc3dThicknessFeaturePtr shell = object;
        if (!shell) return E_NOINTERFACE;
        const auto direction = channel == 0 ? kPositiveExtDir : kNegativeExtDir;
        const mcsWorkID variable = shell->getFaceThicknessVarId(WID_NULL, direction);
        const double observed = shell->getFaceThickness(WID_NULL, direction);
        if (!std::isfinite(observed)) return E_FAIL;
        *value = observed;
        return WriteId(variable, variableGuid, variableCapacity);
    }
    catch (...)
    {
        return E_UNEXPECTED;
    }
}

// Returns the actual SDK placement enum for independent managed verification.
// Existing IDs only: getVar1/getVar2 never call createVariables.
// getParam2 is radians for Radial (3DCore.h:2031); document-variable units must
// be proved separately by its adapter.
extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_GetHolePlacementVariables412(
    const wchar_t* documentGuid, const wchar_t* featureGuid, int* definition,
    wchar_t* firstGuid, int firstCapacity, double* firstValue,
    wchar_t* secondGuid, int secondCapacity, double* secondValue)
{
    if (!definition || !firstValue || !secondValue) return E_POINTER;
    *definition = 0;
    *firstValue = NAN;
    *secondValue = NAN;
    HRESULT status = PrepareBuffer(firstGuid, firstCapacity);
    if (FAILED(status)) return status;
    status = PrepareBuffer(secondGuid, secondCapacity);
    if (FAILED(status)) return status;
    try
    {
        IMcObjectPtr object;
        status = ReadObject(documentGuid, featureGuid, object);
        if (FAILED(status)) return status;
        IMc3dHolePtr hole = object;
        if (!hole) return E_NOINTERFACE;
        m3dIPPDefPtr placement = hole->getPlacement();
        if (!placement) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        const mcsWorkID first = placement->getVar1();
        const mcsWorkID second = placement->getVar2();
        const double observedFirst = placement->getParam1();
        const double observedSecond = placement->getParam2();
        // An inactive placement parameter may be unavailable. Do not turn it
        // into an active-role requirement; the managed typed adapter checks
        // finiteness only for the roles it actually admits.
        *definition = static_cast<int>(placement->defType());
        *firstValue = observedFirst;
        *secondValue = observedSecond;
        status = WriteId(first, firstGuid, firstCapacity);
        if (FAILED(status)) return status;
        status = WriteId(second, secondGuid, secondCapacity);
        return FAILED(status) ? status : S_OK;
    }
    catch (...)
    {
        return E_UNEXPECTED;
    }
}
