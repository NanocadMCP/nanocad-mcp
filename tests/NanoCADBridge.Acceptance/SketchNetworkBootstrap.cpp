// PRIVATE DIAGNOSTIC ONLY. Not linked into or distributed with the product.
// The public SDK declares the force-create factory but only forward-declares
// its returned manager. Keep at most two owned references for this dedicated
// test process. Never guess a base-class cast, vtable slot or Release ABI.
// No unload/reuse or production acceptance until manager ownership can be
// implemented with the complete vendor interface. OS process teardown reclaims
// these references; this is deliberately not a claim of normal SDK cleanup.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <tchar.h>
#include <cstdint>
#include <comdef.h>
// Public external-module configuration from SDK McsAddin.h:18-25.
#define _MCS_CORE_ONLY
#define NOACGE
#define MCGEL
#include "IMcs.h"

namespace {
    IMcs2dConstraintsManager* retained[2]{};
    mcsWorkID used[2];
    unsigned attempts=0;
}

extern "C" __declspec(dllexport) int __cdecl EnsurePrivateSketchNetwork(const wchar_t* sketchText)
{
    if (!sketchText || wcslen(sketchText)!=38 || !gpMcNativeGate || attempts>=2) return -1;
    mcsWorkID sketch;
    if (!sketch.setStrID(sketchText) || sketch.isNull()) return -2;
    for (unsigned i=0;i<attempts;i++) if (used[i]==sketch) return -3;
    unsigned slot=attempts++;
    used[slot]=sketch; // The same sketch can never be retried, even on null/error.
    try {
        retained[slot]=gpMcNativeGate->Get2dConstraintsManager2(kMc2dCtrNet_Auto,sketch,nullptr,true);
        return retained[slot] ? 1 : 0;
    }
    catch (...) { return -4; }
}
