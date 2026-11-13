// Copyright 2026 Andrey Juliovich Camejo Severinov. Licensed under MIT.
#pragma once

#include <Windows.h>
#include <cstdint>

#if defined(NANOCAD_ACCESSIBILITY_BUILD)
#define NANOCAD_ACCESSIBILITY_API __declspec(dllexport)
#else
#define NANOCAD_ACCESSIBILITY_API __declspec(dllimport)
#endif

namespace NanoCAD::Accessibility
{
    inline constexpr std::uint32_t AbiVersion = 1;

    enum class Phase : std::uint32_t
    {
        Idle = 0,
        Started = 1,
        Stopped = 2,
        StartFailed = 3,
        StopFailed = 4
    };

    struct Report
    {
        std::uint32_t Size;
        std::uint32_t Version;
        Phase CurrentPhase;
        HRESULT Status;
        std::uint32_t OwnerThreadId;
        std::uint32_t AnnotatedWindowCount;
        std::uint32_t CallbackCount;
        std::uint32_t GuardedCallbackCount;
        std::uint32_t WrongThreadCallbackCount;
        std::uint32_t IdentityFailureCount;
    };

    static_assert(sizeof(Report) == 40);
}

extern "C"
{
    NANOCAD_ACCESSIBILITY_API std::uint32_t __cdecl NanoCAD_AccessibilityGuardAbiVersion() noexcept;

    // frameProvider must be the host SDK CMDIFrameWnd* (__cdecl*)() function.
    // Start and Stop must run on that frame's owning UI thread. The report
    // input must have Size=sizeof(Report), Version=AbiVersion.
    NANOCAD_ACCESSIBILITY_API HRESULT __cdecl NanoCAD_AccessibilityGuardStart(
        void* frameProvider,
        const wchar_t* logPath,
        NanoCAD::Accessibility::Report* report) noexcept;

    NANOCAD_ACCESSIBILITY_API HRESULT __cdecl NanoCAD_AccessibilityGuardStop(
        NanoCAD::Accessibility::Report* report) noexcept;
}
