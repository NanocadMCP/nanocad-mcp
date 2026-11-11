// Public MultiCAD operations omitted by the managed wrappers.
// The owning WriteJob invokes each export on the CAD document thread.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <limits>
#include <string>

#define _MCS_CORE_ONLY
#define NOACGE
#define MCGEL
// Reuse the validated complete public interfaces without changing the
// read-only NativeParameters412 module or reconstructing SDK vtables.
#include "../NanoCAD.NativeParameters/sdk-compat/generated/IMcObjects.h"
#include "../NanoCAD.NativeParameters/sdk-compat/generated/IMcDocuments.h"
#include "../NanoCAD.NativeParameters/sdk-compat/generated/3DCore.h"
// SDK26 refers to this native MAPI enum but omits its declaration. The installed
// primary MAPI assemblies expose the global type with value__ FIELD I4 (0608).
// An opaque declaration supplies that proved width without invented values or
// changes to any vendor member, complete interface, inheritance or virtual order.
enum McsDimTolVertJustifyEnum : int;
static_assert(sizeof(McsDimTolVertJustifyEnum) == sizeof(std::int32_t));
#include "../NanoCAD.NativeParameters/sdk-compat/generated/IMcDimension.h"
// Both installed primary MAPI assemblies publish these NativeCppClass sizes.
// See artifacts/continuation-259-build/native-dimension-data-abi.json.
static_assert(sizeof(McDimensionData) == 1608, "Installed native dimension-data ABI differs from the supplied SDK.");
static_assert(sizeof(McGeometryParam) == 208, "Installed native geometry-parameter ABI differs from the supplied SDK.");
static_assert(sizeof(CTechSize) == 40, "Installed native tolerance-value ABI differs from the supplied SDK.");

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

// Read the public McDimensionData interface, including its actual three stored
// McGeometryParam slots (IMcDimension.h:825-981). No dimension update is called.
// Text contains ten consecutive strings: view; three sources; three complex
// paths; three viewport IDs. Their lengths are explicit, without terminators.
extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_ReadProjectedDimensionAssociationV2(const wchar_t* documentGuid, const wchar_t* dimensionGuid,
    int* state, int* flags, int* markers, double* parameters, std::int64_t* data,
    double* points, int* textLengths, wchar_t* text, int textCapacity, int* requiredText)
{
    if (!state || !flags || !markers || !parameters || !data || !points || !textLengths ||
        !requiredText || textCapacity < 0 || (textCapacity != 0 && !text)) return E_INVALIDARG;
    std::memset(state, 0, 5 * sizeof(int));
    std::memset(flags, 0, 12 * sizeof(int));
    std::memset(markers, 0, 3 * sizeof(int));
    std::memset(parameters, 0, 3 * sizeof(double));
    std::memset(data, 0, 3 * sizeof(std::int64_t));
    std::memset(points, 0, 18 * sizeof(double));
    std::memset(textLengths, 0, 10 * sizeof(int));
    *requiredText = 0;
    try
    {
        IMcObjectPtr object;
        HRESULT status = ReadObject(documentGuid, dimensionGuid, object);
        if (FAILED(status)) return status;
        IMcDimensionPtr dimension = object;
        McDimensionDataPtr stored = object;
        if (!dimension || !stored) return E_NOINTERFACE;
        mcsWorkID viewId;
        int indicator = 0;
        state[1] = stored->m_iDimtype;
        state[3] = dimension->getAssociative() ? 1 : 0;
        const bool nativeView = stored->isSetToDrawingView(&viewId, &indicator);
        state[4] = nativeView ? 1 : 0;
        state[2] = indicator; // SDK does not document whether this is a count or mask.
        if (!nativeView)
        {
            // isSetToDrawingView is not documented as a complete inventory of
            // every dimension family's stored references. Query the actual
            // public anchors, never a guessed subtype/marker/coordinate alias.
            viewId.setNull();
            mcsWorkID ownerId;
            status = ParseId(documentGuid, ownerId);
            if (FAILED(status)) return status;
            for (const McGeometryParam& anchor : stored->m_Snaps)
            {
                if (anchor.isNull() || anchor.getID().isNull()) continue;
                IMcObjectPtr candidate = gpMcObjManager->getObject(anchor.getID());
                if (!candidate || candidate->ID() != anchor.getID()) continue;
                IMc3dDrawingViewPtr view = candidate;
                if (!view) continue;
                state[0] = 1; // Recognized actual view reference, even if its owner check fails.
                IMcDbEntityPtr viewEntity = candidate;
                if (!viewEntity || viewEntity->getDocumentID() != ownerId) return E_INVALIDARG;
                if (!viewId.isNull() && viewId != candidate->ID()) return E_INVALIDARG;
                viewId = candidate->ID();
            }
            if (viewId.isNull()) return S_FALSE;
        }
        state[0] = 1;
        if (viewId.isNull()) return E_FAIL;
        auto IdText = [](const mcsWorkID& id) -> std::wstring
        {
            if (id.isNull()) return {};
            wchar_t result[GuidCapacity]{};
            if (!id.getStrID(result, GuidCapacity)) throw E_FAIL;
            return result;
        };
        std::wstring strings[10];
        strings[0] = IdText(viewId);
        // This bound is the actual public m_Snaps[3] member, not a product limit.
        static_assert(sizeof(stored->m_Snaps) / sizeof(stored->m_Snaps[0]) == 3);
        for (int slot = 0; slot < 3; ++slot)
        {
            const McGeometryParam& anchor = stored->m_Snaps[slot];
            flags[slot * 4] = anchor.isNull() ? 1 : 0;
            flags[slot * 4 + 2] = S_FALSE;
            flags[slot * 4 + 3] = S_FALSE;
            if (anchor.isNull()) continue;
            strings[1 + slot] = IdText(anchor.getID());
            flags[slot * 4 + 1] = anchor.getComplexID().isSimple() ? 0 : 1;
            if (flags[slot * 4 + 1])
            {
                const McsString path = anchor.getComplexID().getPathAsString();
                strings[4 + slot] = static_cast<LPCWSTR>(path);
            }
            strings[7 + slot] = IdText(anchor.idViewport);
            markers[slot] = anchor.iGSMarker;
            parameters[slot] = anchor.rParam;
            data[slot] = anchor.iData;
            mcsPoint evaluated(0, 0, 0), dimensionSnap(0, 0, 0);
            const HRESULT evaluatedStatus = anchor.getPoint(evaluated, nullptr, McGeometryParam::kPaperSpace, true);
            // SDK GetSnapPoint considers the drawing-view flag but does not
            // specify its output frame. Return it unconverted, without a WCS claim.
            const bool snapAvailable = stored->GetSnapPoint(slot, dimensionSnap);
            flags[slot * 4 + 2] = evaluatedStatus;
            flags[slot * 4 + 3] = snapAvailable ? S_OK : E_FAIL;
            const mcsPoint values[2] = { evaluated, dimensionSnap };
            for (int kind = 0; kind < 2; ++kind)
            {
                if ((kind == 0 && FAILED(evaluatedStatus)) || (kind == 1 && !snapAvailable)) continue;
                const auto& point = values[kind];
                if (!std::isfinite(point.x) || !std::isfinite(point.y) || !std::isfinite(point.z))
                { flags[slot * 4 + 2 + kind] = E_FAIL; continue; }
                points[slot * 6 + kind * 3] = point.x;
                points[slot * 6 + kind * 3 + 1] = point.y;
                points[slot * 6 + kind * 3 + 2] = point.z;
            }
        }
        std::size_t required = 0;
        for (int i = 0; i < 10; ++i)
        {
            if (strings[i].size() > static_cast<std::size_t>((std::numeric_limits<int>::max)())) return E_OUTOFMEMORY;
            textLengths[i] = static_cast<int>(strings[i].size());
            required += strings[i].size();
        }
        if (required > static_cast<std::size_t>((std::numeric_limits<int>::max)())) return E_OUTOFMEMORY;
        *requiredText = static_cast<int>(required);
        if (textCapacity < *requiredText) return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
        int offset = 0;
        for (int i = 0; i < 10; ++i)
        {
            if (textLengths[i]) std::memcpy(text + offset, strings[i].data(), textLengths[i] * sizeof(wchar_t));
            offset += textLengths[i];
        }
        return S_OK;
    }
    catch (HRESULT error) { return error; }
    catch (...) { return E_UNEXPECTED; }
}

// Keep the original three-state ABI and classifier semantics for older bridge
// callers. The new bridge requests V2 explicitly, so mixed native bytes cannot
// silently reinterpret an expanded state buffer.
extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_ReadProjectedDimensionAssociation(const wchar_t* documentGuid, const wchar_t* dimensionGuid,
    int* state, int* flags, int* markers, double* parameters, std::int64_t* data,
    double* points, int* textLengths, wchar_t* text, int textCapacity, int* requiredText)
{
    if (!state) return E_INVALIDARG;
    int observed[5]{};
    const HRESULT status = NanoCAD_ReadProjectedDimensionAssociationV2(documentGuid, dimensionGuid,
        observed, flags, markers, parameters, data, points, textLengths, text, textCapacity, requiredText);
    std::memcpy(state, observed, 3 * sizeof(int));
    if (observed[0] && !observed[4]) { state[0] = 0; return S_FALSE; }
    return status;
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
NanoCAD_ReadHoleControlState(const wchar_t* documentGuid, const wchar_t* bodyGuid,
    const wchar_t* featureGuid, const wchar_t* variableGuid, std::uint32_t* category, int* extent)
{
    if (!category || !extent) return E_INVALIDARG;
    *category = 0; *extent = -1;
    try
    {
        IMcObjectPtr object;
        HRESULT status = ReadObject(documentGuid, featureGuid, object);
        if (FAILED(status)) return status;
        mcsWorkID bodyId, variableId;
        if (FAILED(ParseId(bodyGuid, bodyId)) || FAILED(ParseId(variableGuid, variableId))) return E_INVALIDARG;
        IMc3dHolePtr hole = object;
        if (!hole || hole->getHostSolidID() != bodyId) return E_INVALIDARG;
        *category = hole->getVarValueType(variableId);
        *extent = static_cast<int>(hole->getExtent());
        return S_OK;
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

// Read the original native parameters of two opposite points of the same
// verified projected circle. Line-end fallback parameters do not apply here.
extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_ReadProjectedDiameterAnchors(const wchar_t* documentGuid, const wchar_t* viewGuid,
    int marker, const double* endpoints, double* parameters, std::int64_t* data)
{
    if (!endpoints || !parameters || !data || marker < 0) return E_INVALIDARG;
    for (int i = 0; i < 6; ++i) if (!std::isfinite(endpoints[i])) return E_INVALIDARG;
    try
    {
        IMcObjectPtr object;
        HRESULT status = ReadObject(documentGuid, viewGuid, object);
        if (FAILED(status)) return status;
        IMc3dDrawingViewPtr view = object;
        if (!view) return E_NOINTERFACE;
        const mcsComplexWorkID simpleView(object->ID());
        for (int i = 0; i < 2; ++i)
        {
            const mcsPoint endpoint(endpoints[3 * i], endpoints[3 * i + 1], endpoints[3 * i + 2]);
            McGeometryParam anchor;
            if (!view->getOrigParamAtPoint(endpoint, anchor) || anchor.isNull())
                return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
            if (anchor.getID() != object->ID() || anchor.getComplexID() != simpleView ||
                anchor.isInViewport() || anchor.iGSMarker != marker)
                return E_NOINTERFACE;
            mcsPoint evaluated, original;
            if (FAILED(anchor.getPoint(evaluated)) || evaluated.distanceTo(endpoint) > 1e-7 ||
                !view->getOrigGeomByParam(anchor, original) || original.distanceTo(endpoint) > 1e-7)
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            parameters[i] = anchor.rParam;
            data[i] = anchor.iData;
        }
        return S_OK;
    }
    catch (...) { return E_UNEXPECTED; }
}

// Preserve the original circular primitive when resolving finite arc ends.
// Curve-inference data comes from the SDK; no line-end parameter constants
// are transferred to a circular arc.
extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_ReadProjectedCircularArcAnchors(const wchar_t* documentGuid, const wchar_t* viewGuid,
    int marker, const double* points, const double* observedParameters, double* parameters, std::int64_t* data)
{
    if (!points || !observedParameters || !parameters || !data || marker < 0) return E_INVALIDARG;
    for (int i = 0; i < 9; ++i) if (!std::isfinite(points[i])) return E_INVALIDARG;
    try
    {
        IMcObjectPtr object;
        HRESULT status = ReadObject(documentGuid, viewGuid, object);
        if (FAILED(status)) return status;
        IMc3dDrawingViewPtr view = object;
        if (!view) return E_NOINTERFACE;
        const mcsComplexWorkID simpleView(object->ID());
        const mcsPoint interior(points[6], points[7], points[8]);
        McGeometryParam origin;
        if (!view->getOrigParamAtPoint(interior, origin) || origin.isNull() ||
            origin.getID() != object->ID() || origin.getComplexID() != simpleView ||
            origin.isInViewport() || origin.iGSMarker != marker) return E_NOINTERFACE;
        const auto matches = [&](const McGeometryParam& anchor, const mcsPoint& expected)
        {
            if (anchor.isNull() || anchor.getID() != origin.getID() ||
                anchor.getComplexID() != origin.getComplexID() || anchor.idViewport != origin.idViewport ||
                anchor.iGSMarker != marker) return false;
            mcsPoint evaluated, original;
            return SUCCEEDED(anchor.getPoint(evaluated)) && evaluated.distanceTo(expected) <= 1e-7 &&
                view->getOrigGeomByParam(anchor, original) && original.distanceTo(expected) <= 1e-7;
        };
        if (!matches(origin, interior)) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        for (int i = 0; i < 2; ++i)
        {
            const mcsPoint endpoint(points[3 * i], points[3 * i + 1], points[3 * i + 2]);
            McGeometryParam anchor = origin;
            if (FAILED(anchor.setPoint(endpoint)) || !matches(anchor, endpoint))
            {
                if (!view->getOrigParamAtPoint(endpoint, anchor) || !matches(anchor, endpoint))
                {
                    // The managed SDK unique-parameter conversion was already
                    // round-tripped on the actual selected circular primitive.
                    // Keep its original marker/Data and verify both native
                    // evaluators again; never substitute line-end constants.
                    const double parameter = observedParameters[i];
                    if (!std::isfinite(parameter)) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                    anchor = origin;
                    anchor.rParam = parameter;
                    if (!matches(anchor, endpoint)) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
            }
            parameters[i] = anchor.rParam;
            data[i] = anchor.iData;
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
