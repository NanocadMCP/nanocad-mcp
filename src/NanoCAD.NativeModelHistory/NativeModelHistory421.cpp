// Public MultiCAD operations omitted by the managed wrappers.
// The owning WriteJob invokes each export on the CAD document thread.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <cwchar>

#define _MCS_CORE_ONLY
#define NOACGE
#define MCGEL
// Reuse the validated complete public interfaces without changing the
// read-only NativeParameters412 module or reconstructing SDK vtables.
#include "../NanoCAD.NativeParameters/sdk-compat/generated/IMcObjects.h"
#include "../NanoCAD.NativeParameters/sdk-compat/generated/IMcDocuments.h"
#include "../NanoCAD.NativeParameters/sdk-compat/generated/3DCore.h"

extern "C"
{
    extern MCTYP_API IMcObjectsManager* gpMcObjManager;
    extern MCTYP_API IMcDocumentsManager* gpMcDocManager;
}

namespace
{
    constexpr int GuidCapacity = 39;

    HRESULT ParseId(const wchar_t* text, mcsWorkID& id)
    {
        if (!text || wcsnlen_s(text, GuidCapacity) != GuidCapacity - 1 ||
            text[0] != L'{' || text[GuidCapacity - 2] != L'}' ||
            !id.setStrID(text) || id.isNull())
            return E_INVALIDARG;
        return S_OK;
    }

    HRESULT ReadObject(const wchar_t* documentText, const wchar_t* bodyText,
                       IMcObjectPtr& object)
    {
        mcsWorkID documentId, bodyId;
        HRESULT status = ParseId(documentText, documentId);
        if (FAILED(status)) return status;
        status = ParseId(bodyText, bodyId);
        if (FAILED(status)) return status;
        if (!gpMcObjManager || !gpMcDocManager) return E_UNEXPECTED;

        IMcDocumentPtr document = gpMcDocManager->getDocument(documentId);
        if (!document || document->ID() != documentId)
            return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        object = gpMcObjManager->getObject(bodyId);
        if (!object || object->ID() != bodyId)
            return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        IMcDbEntityPtr entity = object;
        if (!entity) return E_NOINTERFACE;
        if (entity->getDocumentID() != document->ID()) return E_INVALIDARG;
        return S_OK;
    }

    // Public value/identity comparison, without geometric tolerance or private
    // layout reads. Copies of McGeometryParam retain their full native state.
    bool SameFrameReference(const McGeometryParam& left, const McGeometryParam& right)
    {
        return left.isNull() == right.isNull() && left.getID() == right.getID() &&
            left.getComplexID() == right.getComplexID() && left.idViewport == right.idViewport &&
            std::memcmp(&left.rParam, &right.rParam, sizeof(double)) == 0 &&
            left.iGSMarker == right.iGSMarker && left.iData == right.iData;
    }
}

extern "C" __declspec(dllexport) std::uint32_t __cdecl
NanoCAD_NativeModelHistoryAbiVersion()
{
    return 1;
}

extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_SetHoleExtent(const wchar_t* documentGuid, const wchar_t* bodyGuid,
    const wchar_t* featureGuid, int through)
{
    if (through != 0 && through != 1) return E_INVALIDARG;
    try
    {
        IMcObjectPtr object;
        HRESULT status = ReadObject(documentGuid, featureGuid, object);
        if (FAILED(status)) return status;
        mcsWorkID bodyId;
        status = ParseId(bodyGuid, bodyId);
        if (FAILED(status)) return status;
        IMc3dHolePtr hole = object;
        if (!hole || hole->getHostSolidID() != bodyId) return E_INVALIDARG;

        const auto extent = through ? kHoleExtType_All : kHoleExtType_Dist;
        hole->setExtent(extent);
        if (through) hole->updateDepth();
        return hole->getExtent() == extent && hole->isFullDepth() == (through != 0) ? S_OK : E_FAIL;
    }
    catch (...) { return E_UNEXPECTED; }
}

extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_ReadProjectedDimensionAnchors(const wchar_t* documentGuid, const wchar_t* viewGuid,
    int marker, const double* endpoints, double* parameters, std::int64_t* data)
{
    if (!endpoints || !parameters || !data || marker < 0) return E_INVALIDARG;
    for (int i = 0; i < 6; i++) if (!std::isfinite(endpoints[i])) return E_INVALIDARG;
    try
    {
        IMcObjectPtr object;
        HRESULT status = ReadObject(documentGuid, viewGuid, object);
        if (FAILED(status)) return status;
        IMc3dDrawingViewPtr view = object;
        if (!view) return E_NOINTERFACE;
        const mcsPoint start(endpoints[0], endpoints[1], endpoints[2]);
        const mcsPoint end(endpoints[3], endpoints[4], endpoints[5]);
        const mcsPoint midpoint((start.x + end.x) / 2, (start.y + end.y) / 2, (start.z + end.z) / 2);
        McGeometryParam origin;
        if (!view->getOrigParamAtPoint(midpoint, origin) || origin.isNull())
            return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        const mcsComplexWorkID simpleView(object->ID());
        if (origin.getID() != object->ID() || origin.getComplexID() != simpleView ||
            origin.isInViewport() || origin.iGSMarker != marker) return E_NOINTERFACE;

        const auto matches = [&](const McGeometryParam& anchor, const mcsPoint& endpoint)
        {
            if (anchor.isNull() || anchor.getID() != origin.getID() ||
                anchor.getComplexID() != origin.getComplexID() || anchor.idViewport != origin.idViewport ||
                anchor.iGSMarker != marker) return false;
            mcsPoint evaluated, original;
            return SUCCEEDED(anchor.getPoint(evaluated)) && evaluated.distanceTo(endpoint) <= 1e-7 &&
                view->getOrigGeomByParam(anchor, original) && original.distanceTo(endpoint) <= 1e-7;
        };
        int index = 0;
        for (const auto& endpoint : {start, end})
        {
            McGeometryParam anchor = origin;
            status = anchor.setPoint(endpoint);
            if (FAILED(status) || !matches(anchor, endpoint))
            {
                // Picking a shared corner can replace the marker with an
                // adjacent line. Evaluate the relative line ends on the
                // already identified primitive without picking another one.
                anchor = origin;
                anchor.rParam = 0;
                if (!matches(anchor, endpoint))
                {
                    anchor.rParam = 1;
                    if (!matches(anchor, endpoint)) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
            }
            parameters[index] = anchor.rParam;
            data[index++] = anchor.iData;
        }
        return S_OK;
    }
    catch (...) { return E_UNEXPECTED; }
}

// Exact read-only public array binding. No factories, setters, marker sync,
// edit-mode transition or pointer crosses this boundary.
extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_ReadPatternDirection439(const wchar_t* documentGuid, const wchar_t* bodyGuid,
    const wchar_t* featureGuid, int dimension, wchar_t* sourceGuid, int capacity,
    double* parameter, int* marker, std::int64_t* data, int* spacingMode, int* reversed)
{
    if (!sourceGuid || capacity < GuidCapacity || !parameter || !marker || !data || !spacingMode || !reversed)
        return E_INVALIDARG;
    sourceGuid[0] = L'\0';
    *parameter = 0; *marker = 0; *data = 0; *spacingMode = 0; *reversed = -1;
    if (dimension < 0 || dimension > 1) return E_INVALIDARG;
    try
    {
        IMcObjectPtr object;
        HRESULT status = ReadObject(documentGuid, featureGuid, object);
        if (FAILED(status)) return status;
        mcsWorkID bodyId;
        status = ParseId(bodyGuid, bodyId);
        if (FAILED(status)) return status;
        IMc3dArrFeatureTypePtr pattern = object;
        if (!pattern || pattern->getHostSolidID() != bodyId) return E_INVALIDARG;
        IMc3dCircPatFeaturePtr circular = object;
        IMc3dRectPatFeaturePtr rectangular = object;
        if ((!circular && !rectangular) || (circular && dimension != 0)) return E_INVALIDARG;
        const McGeometryParam binding = pattern->arr_getDir(dimension);
        if (binding.isNull() || binding.getID().isNull() || !binding.getID().getStrID(sourceGuid, capacity))
            return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        *parameter = binding.rParam; *marker = binding.iGSMarker; *data = binding.iData;
        *spacingMode = static_cast<int>(pattern->arr_getFillingMode(dimension));
        *reversed = pattern->arr_isDirReversed(dimension) ? 1 : 0;
        return S_OK;
    }
    catch (...) { return E_UNEXPECTED; }
}

// Exact, read-only SDK sketch-frame references. The complete public
// IMcSketch/IMcPlanarSketch declarations come from the verified vendor slice.
// No frame calculation, source lookup/healing, setter or pointer crosses the
// C ABI. Null references remain distinct from unsupported complex references.
extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_ReadSketchFrameReferences440(const wchar_t* documentGuid, const wchar_t* bodyGuid,
    const wchar_t* sketchGuid, wchar_t* pointGuid, wchar_t* axisGuid, wchar_t* axis2Guid,
    int capacity, double* parameters, int* markers, std::int64_t* data,
    int* empty, int* complexPresent, int* axisIsX, int* invertAxis, int* unsupportedIndex)
{
    if (!pointGuid || !axisGuid || !axis2Guid || capacity < GuidCapacity ||
        !parameters || !markers || !data || !empty || !complexPresent ||
        !axisIsX || !invertAxis || !unsupportedIndex)
        return E_INVALIDARG;
    wchar_t* ids[] = { pointGuid, axisGuid, axis2Guid };
    for (int index = 0; index != 3; ++index)
    {
        ids[index][0] = L'\0'; parameters[index] = 0; markers[index] = 0;
        data[index] = 0; empty[index] = -1; complexPresent[index] = -1;
    }
    *axisIsX = -1; *invertAxis = -1; *unsupportedIndex = -1;
    try
    {
        IMcObjectPtr bodyObject, sketchObject;
        HRESULT status = ReadObject(documentGuid, bodyGuid, bodyObject);
        if (FAILED(status)) return status;
        IMc3dSolidPtr body = bodyObject;
        if (!body) return E_NOINTERFACE;
        status = ReadObject(documentGuid, sketchGuid, sketchObject);
        if (FAILED(status)) return status;
        IMcPlanarSketchPtr sketch = sketchObject;
        if (!sketch || sketch->getHostSolidID() != body->ID()) return E_INVALIDARG;
        McGeometryParam bindings[3];
        bool isX = false, inverted = false;
        // SDK 3DCore.h:4783. A failed old support need not expose getPlane().
        status = sketch->getCoordSystemOnPlane(bindings[0], bindings[1], bindings[2], isX, inverted);
        if (status != S_OK) return FAILED(status) ? status : HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        for (int index = 0; index != 3; ++index)
        {
            const McGeometryParam& binding = bindings[index];
            const mcsWorkID& id = binding.getID();
            const mcsComplexWorkID& complex = binding.getComplexID();
            // This part-only adapter cannot prove a viewport or block/xref
            // path by returning only getID() (which may be WID_NULL for it).
            if (!binding.idViewport.isNull() ||
                (!complex.isNull() && (!complex.isSimple() || complex != id)))
            {
                *unsupportedIndex = index;
                return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
            }
            if (binding.isNull() != id.isNull() || (id.isNull() && !complex.isNull()))
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            if (!id.isNull() && !id.getStrID(ids[index], capacity))
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            parameters[index] = binding.rParam; markers[index] = binding.iGSMarker;
            data[index] = binding.iData; empty[index] = binding.isNull() ? 1 : 0;
            complexPresent[index] = complex.isNull() ? 0 : 1;
        }
        *axisIsX = isX ? 1 : 0; *invertAxis = inverted ? 1 : 0;
        return S_OK;
    }
    catch (...) { return E_UNEXPECTED; }
}

// Preserve the actual associative frame when setPlanarEntityID clears it.
// All three native McGeometryParam values stay in this one call; no managed
// reconstruction or guessed coordinate data participates. Phase/HRESULTs
// distinguish a started partial failure from a completed verified return.
static HRESULT ReassignSketchSupport440(const wchar_t* documentGuid, const wchar_t* bodyGuid,
    const wchar_t* sketchGuid, const wchar_t* expectedSupportGuid, const wchar_t* targetSupportGuid,
    int* phase, int* supportResult, int* frameResult, int* sketchUpdateResult, int* updateAllResult, bool useTargetFrame)
{
    if (!phase || !supportResult || !frameResult || !sketchUpdateResult || !updateAllResult) return E_POINTER;
    *phase = 0; *supportResult = E_PENDING; *frameResult = E_PENDING;
    *sketchUpdateResult = E_PENDING; *updateAllResult = E_PENDING;
    try
    {
        IMcObjectPtr bodyObject, sketchObject, targetObject;
        HRESULT status = ReadObject(documentGuid, bodyGuid, bodyObject);
        if (FAILED(status)) return status;
        IMc3dSolidPtr body = bodyObject;
        if (!body) return E_NOINTERFACE;
        status = ReadObject(documentGuid, sketchGuid, sketchObject);
        if (FAILED(status)) return status;
        IMcPlanarSketchPtr sketch = sketchObject;
        if (!sketch || sketch->getHostSolidID() != body->ID() || sketch->isInEditMode()) return E_INVALIDARG;
        mcsWorkID expectedSupport, targetSupport;
        status = ParseId(expectedSupportGuid, expectedSupport);
        if (FAILED(status)) return status;
        status = ParseId(targetSupportGuid, targetSupport);
        if (FAILED(status)) return status;
        if (sketch->getPlanarEntityID() != expectedSupport || expectedSupport == targetSupport)
            return HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH);
        status = ReadObject(documentGuid, targetSupportGuid, targetObject);
        if (FAILED(status)) return status;
        IMc3dWorkPlanePtr target = targetObject;
        if (!target || (!target->getHostSolidID().isNull() && target->getHostSolidID() != body->ID()))
            return E_INVALIDARG;
        McGeometryParam original[3];
        bool isX = false, inverted = false;
        status = sketch->getCoordSystemOnPlane(original[0], original[1], original[2], isX, inverted);
        if (status != S_OK) return FAILED(status) ? status : HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        // This is the same part-only observation boundary used by the strict
        // managed before snapshot. Never treat a block/viewport path as empty.
        for (const McGeometryParam& binding : original)
        {
            const mcsWorkID& id = binding.getID();
            const mcsComplexWorkID& complex = binding.getComplexID();
            if (!binding.idViewport.isNull() ||
                (!complex.isNull() && (!complex.isSimple() || complex != id)))
                return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
            if (binding.isNull() != id.isNull() || (id.isNull() && !complex.isNull()))
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        }
        *phase = 1; // exact original frame captured; setters not entered
        *phase = 2; // support setter entered; a failure may be partial
        *supportResult = sketch->setPlanarEntityID(targetSupport);
        if (*supportResult != S_OK)
            return FAILED(*supportResult) ? *supportResult : HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        *phase = 3; // support setter returned S_OK
        if (sketch->getPlanarEntityID() != targetSupport) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        // Public update barrier before frame assignment, as in SDK examples
        // after support binding. Keep all original native parameter values on
        // this stack across callbacks. HRESULT success codes are not narrowed
        // to S_OK: these update methods document no such restriction.
        IMcDbEntityPtr sketchEntity = sketchObject;
        if (!sketchEntity) return E_NOINTERFACE;
        *phase = 8; // sketch update entered
        *sketchUpdateResult = sketchEntity->update();
        if (FAILED(*sketchUpdateResult)) return *sketchUpdateResult;
        *phase = 9; // sketch update returned a successful HRESULT
        *phase = 10; // object-manager updateAll entered
        if (!gpMcObjManager) return E_UNEXPECTED;
        *updateAllResult = gpMcObjManager->updateAll();
        if (FAILED(*updateAllResult)) return *updateAllResult;
        *phase = 11; // updateAll returned a successful HRESULT
        // Updates may register/acquire or replace current native interfaces.
        // Prove actual current identities/owner/host/support before restoring
        // the retained frame; never calculate substitute references.
        IMcObjectPtr readyBodyObject, readySketchObject, readyTargetObject;
        status = ReadObject(documentGuid, bodyGuid, readyBodyObject);
        if (FAILED(status)) return status;
        status = ReadObject(documentGuid, sketchGuid, readySketchObject);
        if (FAILED(status)) return status;
        status = ReadObject(documentGuid, targetSupportGuid, readyTargetObject);
        if (FAILED(status)) return status;
        IMc3dSolidPtr readyBody = readyBodyObject;
        IMcPlanarSketchPtr readySketch = readySketchObject;
        IMc3dWorkPlanePtr readyTarget = readyTargetObject;
        if (!readyBody || !readySketch || !readyTarget ||
            readySketch->getHostSolidID() != readyBody->ID() || readySketch->isInEditMode() ||
            readySketch->getPlanarEntityID() != targetSupport ||
            (!readyTarget->getHostSolidID().isNull() && readyTarget->getHostSolidID() != readyBody->ID()))
            return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        sketch = readySketch;
        *phase = 12; // current owner/part/sketch/WorkPlane/support verified
        if (!useTargetFrame)
        {
            *phase = 4; // documented frame setter entered; no fallback/clear retry
            // SDK 3DCore.h:4769. Keep actual original values and exact flags.
            *frameResult = sketch->setCoordSystemOnPlane(original[0], original[1], original[2], isX, false, inverted);
            if (*frameResult != S_OK)
                return FAILED(*frameResult) ? *frameResult : HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            *phase = 5; // frame setter returned S_OK
        }
        // Resolve the actual current native object/owner before readback,
        // rather than assuming the retained interface alone proves current.
        IMcObjectPtr currentObject;
        status = ReadObject(documentGuid, sketchGuid, currentObject);
        if (FAILED(status)) return status;
        IMcPlanarSketchPtr currentSketch = currentObject;
        if (!currentSketch || currentSketch->getHostSolidID() != body->ID() ||
            currentSketch->getPlanarEntityID() != targetSupport)
            return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        McGeometryParam current[3];
        bool currentIsX = false, currentInverted = false;
        status = currentSketch->getCoordSystemOnPlane(current[0], current[1], current[2], currentIsX, currentInverted);
        if (status != S_OK) return FAILED(status) ? status : HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        *phase = 6; // readback returned S_OK
        if (useTargetFrame)
        {
            // Explicit replacement only: support assignment must itself have
            // retired all frame bindings. Never clear/retry a remaining one.
            for (const McGeometryParam& binding : current)
                if (!binding.isNull() || !binding.getID().isNull() ||
                    !binding.getComplexID().isNull() || !binding.idViewport.isNull())
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        }
        else if (currentIsX != isX || currentInverted != inverted ||
            !SameFrameReference(original[0], current[0]) || !SameFrameReference(original[1], current[1]) ||
            !SameFrameReference(original[2], current[2]))
            return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        *phase = 7; // exact public references/flags/support/owner verified
        return S_OK;
    }
    catch (...) { return E_UNEXPECTED; }
}

extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_ReassignSketchSupportPreservingFrameWithUpdate440(const wchar_t* documentGuid, const wchar_t* bodyGuid,
    const wchar_t* sketchGuid, const wchar_t* expectedSupportGuid, const wchar_t* targetSupportGuid,
    int* phase, int* supportResult, int* frameResult, int* sketchUpdateResult, int* updateAllResult)
{
    return ReassignSketchSupport440(documentGuid, bodyGuid, sketchGuid, expectedSupportGuid, targetSupportGuid,
        phase, supportResult, frameResult, sketchUpdateResult, updateAllResult, false);
}

extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_ReassignSketchSupportUsingTargetFrame440(const wchar_t* documentGuid, const wchar_t* bodyGuid,
    const wchar_t* sketchGuid, const wchar_t* expectedSupportGuid, const wchar_t* targetSupportGuid,
    int* phase, int* supportResult, int* sketchUpdateResult, int* updateAllResult)
{
    int unusedFrameResult = E_PENDING;
    return ReassignSketchSupport440(documentGuid, bodyGuid, sketchGuid, expectedSupportGuid, targetSupportGuid,
        phase, supportResult, &unusedFrameResult, sketchUpdateResult, updateAllResult, true);
}

// Preserve the preceding C ABI's arity. The current managed adapter requires
// the additive WithUpdate export so both actual update HRESULTs are observed.
extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_ReassignSketchSupportPreservingFrame440(const wchar_t* documentGuid, const wchar_t* bodyGuid,
    const wchar_t* sketchGuid, const wchar_t* expectedSupportGuid, const wchar_t* targetSupportGuid,
    int* phase, int* supportResult, int* frameResult)
{
    int sketchUpdateResult = E_PENDING, updateAllResult = E_PENDING;
    return NanoCAD_ReassignSketchSupportPreservingFrameWithUpdate440(documentGuid, bodyGuid, sketchGuid,
        expectedSupportGuid, targetSupportGuid, phase, supportResult, frameResult, &sketchUpdateResult, &updateAllResult);
}

// accepted: -1 = no completed native return, 0/1 = actual rebuildPart return.
// S_OK reports an observed native return, including false. It does not claim
// every history item is healthy; the managed owning writer reads that state.
extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_TargetedRebuild421(const wchar_t* documentGuid, const wchar_t* bodyGuid,
                          int* accepted)
{
    if (!accepted) return E_POINTER;
    *accepted = -1;
    try
    {
        IMcObjectPtr object;
        HRESULT status = ReadObject(documentGuid, bodyGuid, object);
        if (FAILED(status)) return status;
        IMc3dSolidPtr body = object;
        if (!body) return E_NOINTERFACE;
        const bool ignoreErrors = body->isIgnoreBuildErrors();

        // SDK 3DCore.h:6003. The managed parameterless wrapper passes true,
        // false. Request the same full rebuild with its public immediate mode.
        // No flag clearing, source setter, UpdateAll, or retry is performed.
        *accepted = body->rebuildPart(true, true) ? 1 : 0;

        // Hold the original interfaces through the entire call, and resolve
        // the current work ID again before comparing actual owner/policy.
        IMcObjectPtr current;
        status = ReadObject(documentGuid, bodyGuid, current);
        if (FAILED(status)) return status;
        IMc3dSolidPtr currentBody = current;
        if (!currentBody) return E_NOINTERFACE;
        if (currentBody->isIgnoreBuildErrors() != ignoreErrors)
            return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        return S_OK;
    }
    catch (...)
    {
        return E_UNEXPECTED;
    }
}
