#pragma once

// SDK26's McsGeometry.h:101 includes this header, absent from its distribution.
// Only the four published names needed to parse the consumed vendor interfaces
// are provided. This is not a replacement/full catalogue of the vendor enum.
//
// Exact Int32 ABI: native EntityGeometryTypeEnum.value__ in SDK and installed
// PE metadata has signature 06 08. Public EntityGeomType is passed unchanged
// by EntityGeometry.GeometryType and FeatureEntity.GetFEV; no value mapping.
// Paramless managed GetFEV loads 0x007FFF03; GetStart/End/SideFEV load 0x007F0000,
// matching the corresponding native default names in 3DCore.h:6221/6360-6380.
// Null and Point match public kNull/kPoint in both supplied/installed metadata.
// See artifacts/audit-parametric412/sdk-compat-proof412.json for exact evidence.
// These geometry enums/APIs are never called or passed across our C ABI.
enum EntityGeometryTypeEnum
{
    kMcsNull = 0,
    kMcsPoint = 1,
    kMcsSurfaceEntities = 0x007F0000,
    kMcsAllEntities = 0x007FFF03
};
static_assert(sizeof(EntityGeometryTypeEnum) == 4, "SDK enum requires Int32");
