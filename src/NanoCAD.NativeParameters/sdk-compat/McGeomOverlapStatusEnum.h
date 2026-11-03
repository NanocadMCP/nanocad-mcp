#pragma once

// SDK26 McsGeometry.h:344 includes this omitted header. The one enumerator
// needed by 3DCore.h:838 has the published GeomOverlappingStatus.NotOverlapped
// value0. The managed overlapping-status getters return the native enum without
// a conversion table. Native McGeomOverlapStatusEnum and its managed wrapper
// both have Int32 underlying storage in SDK and installed PE metadata.
// This partial declaration only makes unused vendor overlap declarations parse;
// our exports do not instantiate findSubentResItem or call an overlap API.
// Evidence: artifacts/audit-parametric412/sdk-compat-proof412.json.
enum McGeomOverlapStatusEnum
{
    kMcGeomOvl_NotOverlapped = 0
};
static_assert(sizeof(McGeomOverlapStatusEnum) == 4, "SDK enum requires Int32");
