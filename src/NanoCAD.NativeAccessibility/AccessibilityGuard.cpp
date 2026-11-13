// Copyright 2026 Andrey Juliovich Camejo Severinov. Licensed under MIT.
#include <afxwin.h>
#include <afxext.h>
#include <afxole.h>
#include <afxtabctrl.h>
#include <afxdllx.h>
#include "AccessibilityGuard.h"
#include "NativeValueImport.h"

#include <oleacc.h>
#include <commctrl.h>
#include <strsafe.h>
#include <new>
#include <typeinfo>
#include <cstring>
#include <cstdint>

namespace
{
    using NanoCAD::Accessibility::Phase;
    using NanoCAD::Accessibility::Report;
    using FrameProvider = CMDIFrameWnd* (__cdecl*)();

    AFX_EXTENSION_MODULE g_extension{};
    SRWLOCK g_sessionLock = SRWLOCK_INIT;
    class PropertyServer;
    struct PendingRegistration;
    struct Session;
    Session* g_session = nullptr;

    // Forming these base-member pointers is permitted by [class.protected].
    // They are applied only to verified real base subobjects, never to a
    // fictional derived object. No member value is changed by this guard.
    class ModuleMemberAccess : public CCmdTarget
    {
    public:
        static constexpr AFX_MODULE_STATE* CCmdTarget::* State() noexcept
        {
            return &ModuleMemberAccess::m_pModuleState;
        }
    };

    class TabMemberAccess : public CMFCBaseTabCtrl
    {
    public:
        static constexpr CPtrArray CMFCBaseTabCtrl::* Tabs() noexcept
        {
            return &TabMemberAccess::m_arTabs;
        }

        using ValueMethod = HRESULT (CMFCBaseTabCtrl::*)(VARIANT, BSTR*);
        static constexpr ValueMethod Value() noexcept
        {
            return &TabMemberAccess::get_accValue;
        }
    };

    template<class T>
    bool ReadMemory(std::uintptr_t address, T* value) noexcept
    {
        SIZE_T read = 0;
        return ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<const void*>(address),
            value, sizeof(T), &read) && read == sizeof(T);
    }

    bool ExecutableImage(std::uintptr_t address) noexcept
    {
        MEMORY_BASIC_INFORMATION region{};
        if (!VirtualQuery(reinterpret_cast<const void*>(address), &region, sizeof(region))
            || region.State != MEM_COMMIT || region.Type != MEM_IMAGE
            || (region.Protect & (PAGE_GUARD | PAGE_NOACCESS))) return false;
        const DWORD access = region.Protect & 0xff;
        return access == PAGE_EXECUTE || access == PAGE_EXECUTE_READ
            || access == PAGE_EXECUTE_READWRITE || access == PAGE_EXECUTE_WRITECOPY;
    }

    // Resolve only compiler/linker unconditional jump veneers. Function bodies
    // are never executed or modified. Unknown bodies remain distinct targets.
    std::uintptr_t ResolveImportThunk(std::uintptr_t address) noexcept
    {
        std::uintptr_t visited[8]{};
        for (unsigned hop = 0; hop < _countof(visited); ++hop)
        {
            for (unsigned index = 0; index < hop; ++index)
                if (visited[index] == address) return 0;
            visited[hop] = address;
            if (!ExecutableImage(address)) return 0;
            BYTE code[16]{};
            if (!ReadMemory(address, &code)) return 0;
            unsigned prefix = 0;
            if (code[0] == 0x48 && code[1] == 0xff && code[2] == 0x25) prefix = 1;
            if (code[prefix] == 0xff && code[prefix + 1] == 0x25)
            {
                std::int32_t displacement = 0;
                std::memcpy(&displacement, code + prefix + 2, sizeof(displacement));
                const std::uintptr_t slot = address + prefix + 6 + displacement;
                if (!ReadMemory(slot, &address) || !address) return 0;
            }
            else if (code[0] == 0xe9)
            {
                std::int32_t displacement = 0;
                std::memcpy(&displacement, code + 1, sizeof(displacement));
                address += std::uintptr_t{ 5 } + displacement;
            }
            else if (code[0] == 0xeb)
            {
                address += 2 + static_cast<std::int8_t>(code[1]);
            }
            else return address;
        }
        return 0;
    }

    bool ValueDispatchOffset(std::uintptr_t* offset) noexcept
    {
        // On the assessed MSVC x64 CMFCBaseTabCtrl member-pointer ABI, this
        // pointer names a compiler-generated dispatch thunk. Derive its slot
        // without assuming the most-derived host class's inheritance/layout.
        static_assert(sizeof(TabMemberAccess::ValueMethod) == sizeof(std::uintptr_t));
        const auto member = TabMemberAccess::Value();
        std::uintptr_t dispatcher = 0;
        std::memcpy(&dispatcher, &member, sizeof(dispatcher));
        dispatcher = ResolveImportThunk(dispatcher);
        BYTE code[16]{};
        if (!dispatcher || !ReadMemory(dispatcher, &code)
            || code[0] != 0x48 || code[1] != 0x8b || code[2] != 0x01) return false;
        unsigned instruction = 3;
        if (code[instruction] == 0x48) ++instruction;
        if (code[instruction] != 0xff) return false;
        if (code[instruction + 1] == 0xa0)
        {
            std::int32_t displacement = 0;
            std::memcpy(&displacement, code + instruction + 2, sizeof(displacement));
            if (displacement < 0) return false;
            *offset = static_cast<std::uintptr_t>(displacement);
        }
        else if (code[instruction + 1] == 0x60)
        {
            if (code[instruction + 2] & 0x80) return false;
            *offset = code[instruction + 2];
        }
        else return false;
        return *offset % sizeof(std::uintptr_t) == 0;
    }

    bool ValueTarget(CMFCBaseTabCtrl* tab, std::uintptr_t offset, std::uintptr_t* target) noexcept
    {
        std::uintptr_t table = 0;
        if (!ReadMemory(reinterpret_cast<std::uintptr_t>(tab), &table)) return false;
        MEMORY_BASIC_INFORMATION region{};
        if (!VirtualQuery(reinterpret_cast<const void*>(table), &region, sizeof(region))
            || region.State != MEM_COMMIT || region.Type != MEM_IMAGE
            || (region.Protect & (PAGE_GUARD | PAGE_NOACCESS))) return false;
        const auto start = reinterpret_cast<std::uintptr_t>(region.BaseAddress);
        if (table < start || table - start > region.RegionSize) return false;
        const SIZE_T available = region.RegionSize - (table - start);
        if (offset > available || sizeof(*target) > available - offset) return false;
        return ReadMemory(table + offset, target);
    }

    struct TabState
    {
        int active = -1;
        INT_PTR arraySize = 0;
        int declaredCount = 0;
        char runtimeType[161] = "-";
    };

    struct Session
    {
        volatile LONG references = 1;
        volatile LONG active = 0;
        volatile LONG annotated = 0;
        volatile LONG callbacks = 0;
        volatile LONG guarded = 0;
        volatile LONG wrongThread = 0;
        volatile LONG identityFailures = 0;
        volatile LONG firstFailure = S_OK;
        DWORD ownerThread = 0;
        DWORD process = 0;
        HWND frameWindow = nullptr;
        FrameProvider provider = nullptr;
        std::uintptr_t valueDispatchOffset = 0;
        std::uintptr_t baseValueGetter = 0;
        IAccPropServices* services = nullptr;
        HHOOK hook = nullptr;
        HANDLE log = INVALID_HANDLE_VALUE;
        SRWLOCK logLock = SRWLOCK_INIT;
        PropertyServer* targets = nullptr;
        PropertyServer* pendingClears = nullptr;
        PendingRegistration* registrations = nullptr;

        void AddRef() noexcept { InterlockedIncrement(&references); }
        void Release() noexcept
        {
            if (InterlockedDecrement(&references) == 0)
            {
                if (log != INVALID_HANDLE_VALUE) CloseHandle(log);
                delete this;
            }
        }
    };

    struct PendingRegistration
    {
        Session* session;
        HWND window;
        PendingRegistration* previous;
        PendingRegistration(Session* owner, HWND target) noexcept
            : session(owner), window(target), previous(owner->registrations)
        {
            session->AddRef();
            session->registrations = this;
        }
        ~PendingRegistration()
        {
            session->registrations = previous;
            session->Release();
        }
    };

    LONG ReadCounter(volatile LONG* value) noexcept
    {
        return InterlockedCompareExchange(value, 0, 0);
    }

    HRESULT LastErrorResult() noexcept
    {
        const DWORD error = GetLastError();
        return HRESULT_FROM_WIN32(error ? error : ERROR_GEN_FAILURE);
    }

    void Log(Session* session, const wchar_t* event, HWND window,
        const TabState& state, HRESULT status = S_OK) noexcept
    {
        if (session->log == INVALID_HANDLE_VALUE) return;
        SYSTEMTIME time{};
        GetSystemTime(&time);
        wchar_t line[640]{};
        if (FAILED(StringCchPrintfW(line, _countof(line),
            L"%04u-%02u-%02uT%02u:%02u:%02u.%03uZ event=%s tid=%lu hwnd=%p active=%d array_size=%lld declared_count=%d class=%.160hs hr=0x%08lX\r\n",
            time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute,
            time.wSecond, time.wMilliseconds, event, GetCurrentThreadId(),
            window, state.active, static_cast<long long>(state.arraySize),
            state.declaredCount, state.runtimeType, static_cast<unsigned long>(status)))) return;
        char utf8[1920]{};
        const int bytes = WideCharToMultiByte(CP_UTF8, 0, line, -1,
            utf8, static_cast<int>(sizeof(utf8)), nullptr, nullptr);
        if (bytes <= 1) return;
        AcquireSRWLockExclusive(&session->logLock);
        DWORD written = 0;
        WriteFile(session->log, utf8, static_cast<DWORD>(bytes - 1), &written, nullptr);
        ReleaseSRWLockExclusive(&session->logLock);
    }

    void RecordFailure(Session* session, HRESULT failure) noexcept
    {
        if (FAILED(failure)) InterlockedCompareExchange(&session->firstFailure, failure, S_OK);
    }

    Session* AcquireSession() noexcept
    {
        AcquireSRWLockShared(&g_sessionLock);
        Session* session = g_session;
        if (session) session->AddRef();
        ReleaseSRWLockShared(&g_sessionLock);
        return session;
    }

    bool ValidReport(const Report* report) noexcept
    {
        return report && report->Size == sizeof(Report)
            && report->Version == NanoCAD::Accessibility::AbiVersion;
    }

    void WriteReport(Report* report, Session* session, Phase phase, HRESULT status) noexcept
    {
        *report = { sizeof(Report), NanoCAD::Accessibility::AbiVersion, phase, status,
            session ? session->ownerThread : 0,
            session ? static_cast<DWORD>(ReadCounter(&session->annotated)) : 0,
            session ? static_cast<DWORD>(ReadCounter(&session->callbacks)) : 0,
            session ? static_cast<DWORD>(ReadCounter(&session->guarded)) : 0,
            session ? static_cast<DWORD>(ReadCounter(&session->wrongThread)) : 0,
            session ? static_cast<DWORD>(ReadCounter(&session->identityFailures)) : 0 };
    }

    HRESULT FrameContext(FrameProvider provider, DWORD expectedThread, DWORD expectedProcess,
        HWND expectedFrame, AFX_MODULE_STATE** state, HWND* frameWindow) noexcept
    {
        CMDIFrameWnd* frame = provider();
        if (!frame) return E_PENDING;
        HWND window = frame->GetSafeHwnd();
        if (!window) return E_PENDING;
        DWORD process = 0;
        const DWORD thread = GetWindowThreadProcessId(window, &process);
        if (!thread || process != expectedProcess) return CO_E_OBJNOTCONNECTED;
        if (thread != expectedThread) return RPC_E_WRONG_THREAD;
        if (expectedFrame && window != expectedFrame) return CO_E_OBJNOTCONNECTED;
        AFX_MODULE_STATE* module = frame->*ModuleMemberAccess::State();
        if (!module) return CO_E_OBJNOTCONNECTED;
        AFX_MAINTAIN_STATE2 scope(module);
        if (CWnd::FromHandlePermanent(window) != frame) return CO_E_OBJNOTCONNECTED;
        *state = module;
        *frameWindow = window;
        return S_OK;
    }

    HRESULT ReadTabState(Session* session, HWND window, TabState* result) noexcept
    {
        if (GetCurrentThreadId() != session->ownerThread) return RPC_E_WRONG_THREAD;
        DWORD process = 0;
        const DWORD thread = GetWindowThreadProcessId(window, &process);
        if (thread != session->ownerThread || process != session->process) return S_FALSE;
        AFX_MODULE_STATE* module = nullptr;
        HWND frame = nullptr;
        const HRESULT frameStatus = FrameContext(session->provider, session->ownerThread,
            session->process, session->frameWindow, &module, &frame);
        if (FAILED(frameStatus)) return frameStatus;
        if (!IsChild(frame, window)) return S_FALSE;
        AFX_MAINTAIN_STATE2 scope(module);
        CWnd* object = CWnd::FromHandlePermanent(window);
        if (!object || !object->IsKindOf(RUNTIME_CLASS(CMFCBaseTabCtrl))) return S_FALSE;
        CMFCBaseTabCtrl* tab = nullptr;
        try
        {
            tab = dynamic_cast<CMFCBaseTabCtrl*>(object);
        }
        catch (const std::__non_rtti_object&)
        {
            return S_FALSE;
        }
        if (!tab || tab->GetSafeHwnd() != window
            || tab->*ModuleMemberAccess::State() != module) return S_FALSE;
        std::uintptr_t target = 0;
        if (!ValueTarget(tab, session->valueDispatchOffset, &target)
            || ResolveImportThunk(target) != session->baseValueGetter) return S_FALSE;
        const CPtrArray& tabs = tab->*TabMemberAccess::Tabs();
        result->active = tab->CMFCBaseTabCtrl::GetActiveTab();
        result->arraySize = tabs.GetSize();
        result->declaredCount = tab->CMFCBaseTabCtrl::GetTabsNum();
        // Retain log text, not a provider module's RTTI pointer, across COM.
        StringCchCopyNA(result->runtimeType, _countof(result->runtimeType), typeid(*tab).raw_name(), 160);
        return S_OK;
    }

    LRESULT CALLBACK DestroySubclass(HWND, UINT, WPARAM, LPARAM, UINT_PTR, DWORD_PTR);
    void Disconnect(PropertyServer* server) noexcept;

    class PropertyServer final : public IAccPropServer
    {
    public:
        PropertyServer(Session* owner, HWND window) noexcept : session(owner), hwnd(window)
        {
            session->AddRef();
        }

        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) noexcept override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (iid == IID_IUnknown || iid == __uuidof(IAccPropServer))
            {
                *value = static_cast<IAccPropServer*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        ULONG STDMETHODCALLTYPE AddRef() noexcept override
        {
            return static_cast<ULONG>(InterlockedIncrement(&references));
        }

        ULONG STDMETHODCALLTYPE Release() noexcept override
        {
            const LONG remaining = InterlockedDecrement(&references);
            if (!remaining) delete this;
            return static_cast<ULONG>(remaining);
        }

        HRESULT STDMETHODCALLTYPE GetPropValue(const BYTE* identity, DWORD identitySize,
            MSAAPROPID property, VARIANT* value, BOOL* hasProperty) noexcept override
        {
            AddRef();
            struct CallReference
            {
                PropertyServer* server;
                ~CallReference() { server->Release(); }
            } callReference{ this };
            if (!value || !hasProperty) return E_POINTER;
            VariantInit(value);
            *hasProperty = FALSE;
            InterlockedIncrement(&session->callbacks);
            if (GetCurrentThreadId() != session->ownerThread)
            {
                InterlockedIncrement(&session->wrongThread);
                if (!InterlockedExchange(&wrongThreadLogged, 1))
                    Log(session, L"wrong-thread", hwnd, {}, RPC_E_WRONG_THREAD);
                return RPC_E_WRONG_THREAD;
            }
            if (!ReadCounter(&session->active) || !ReadCounter(&connected)
                || !session->services) return CO_E_OBJNOTCONNECTED;
            HWND requestWindow = nullptr;
            DWORD objectId = 0;
            DWORD childId = 0;
            IAccPropServices* services = annotationServices;
            services->AddRef();
            HRESULT status = services->DecomposeHwndIdentityString(identity,
                identitySize, &requestWindow, &objectId, &childId);
            services->Release();
            if (!ReadCounter(&session->active) || !ReadCounter(&connected))
                return CO_E_OBJNOTCONNECTED;
            if (FAILED(status) || requestWindow != hwnd || objectId != static_cast<DWORD>(OBJID_CLIENT)
                || childId != CHILDID_SELF || property != PROPID_ACC_VALUE)
            {
                InterlockedIncrement(&session->identityFailures);
                if (!InterlockedExchange(&identityLogged, 1))
                    Log(session, L"identity-failed", hwnd, {}, E_INVALIDARG);
                return E_INVALIDARG;
            }
            TabState state{};
            status = ReadTabState(session, hwnd, &state);
            if (status != S_OK)
            {
                InterlockedIncrement(&session->identityFailures);
                if (!InterlockedExchange(&identityLogged, 1))
                    Log(session, L"identity-failed", hwnd, state, CO_E_OBJNOTCONNECTED);
                return CO_E_OBJNOTCONNECTED;
            }
            if (!InterlockedExchange(&callbackLogged, 1)) Log(session, L"callback", hwnd, state);
            if (state.active != -1 && (state.active < 0 || state.active >= state.arraySize))
            {
                BSTR empty = SysAllocStringLen(nullptr, 0);
                if (!empty) return E_OUTOFMEMORY;
                value->vt = VT_BSTR;
                value->bstrVal = empty;
                *hasProperty = TRUE;
                InterlockedIncrement(&session->guarded);
                if (!InterlockedExchange(&invalidLogged, 1)) Log(session, L"guarded", hwnd, state);
            }
            // FALSE with VT_EMPTY intentionally delegates every normal state,
            // including active==-1, to the original accessible provider.
            return S_OK;
        }

        Session* session;
        HWND hwnd;
        PropertyServer* next = nullptr;
        PropertyServer* pendingNext = nullptr;
        bool registered = false;
        bool pendingClear = false;
        bool clearInProgress = false;
        bool pendingCounted = false;
        bool subclassReference = false;
        BYTE* annotationIdentity = nullptr;
        DWORD annotationIdentitySize = 0;
        IAccPropServices* annotationServices = nullptr;
        volatile LONG connected = 1;

    private:
        ~PropertyServer()
        {
            CoTaskMemFree(annotationIdentity);
            if (annotationServices) annotationServices->Release();
            session->Release();
        }
        volatile LONG references = 1;
        volatile LONG callbackLogged = 0;
        volatile LONG invalidLogged = 0;
        volatile LONG wrongThreadLogged = 0;
        volatile LONG identityLogged = 0;
    };

    HRESULT ClearPending(PropertyServer* server) noexcept
    {
        if (!server->pendingClear) return S_OK;
        if (server->clearInProgress) return E_PENDING;
        server->AddRef();
        server->clearInProgress = true;
        Session* session = server->session;
        if (server->pendingCounted)
        {
            server->pendingCounted = false;
            InterlockedDecrement(&session->annotated);
        }
        IAccPropServices* services = server->annotationServices;
        const MSAAPROPID property = PROPID_ACC_VALUE;
        const HRESULT status = services->ClearProps(server->annotationIdentity,
            server->annotationIdentitySize, &property, 1);
        server->clearInProgress = false;
        if (SUCCEEDED(status))
        {
            PropertyServer** link = &session->pendingClears;
            while (*link && *link != server) link = &(*link)->pendingNext;
            if (*link)
            {
                *link = server->pendingNext;
                server->pendingNext = nullptr;
                server->pendingClear = false;
                server->Release(); // The pending-clear list's reference.
            }
        }
        else
        {
            server->pendingCounted = true;
            InterlockedIncrement(&session->annotated);
            RecordFailure(session, status);
            Log(session, L"clear-failed", server->hwnd, {}, status);
        }
        server->Release();
        return status;
    }

    void Disconnect(PropertyServer* server) noexcept
    {
        server->AddRef();
        Session* session = server->session;
        InterlockedExchange(&server->connected, 0);
        DWORD_PTR reference = 0;
        const UINT_PTR id = reinterpret_cast<UINT_PTR>(server);
        const bool ownsSubclass = GetWindowSubclass(server->hwnd, DestroySubclass, id, &reference)
            && reference == reinterpret_cast<DWORD_PTR>(server);
        const bool wasRegistered = server->registered;
        server->registered = false;
        if (wasRegistered) InterlockedDecrement(&session->annotated);
        if (wasRegistered && !server->pendingClear)
        {
            server->pendingClear = true;
            server->pendingNext = session->pendingClears;
            session->pendingClears = server;
            server->AddRef();
        }
        bool subclassRemoved = !ownsSubclass;
        if (ownsSubclass)
        {
            subclassRemoved = RemoveWindowSubclass(server->hwnd, DestroySubclass, id) != FALSE;
            if (!subclassRemoved)
            {
                const HRESULT status = LastErrorResult();
                RecordFailure(session, status);
                Log(session, L"subclass-remove-failed", server->hwnd, {}, status);
            }
        }
        if (subclassRemoved && server->subclassReference)
        {
            server->subclassReference = false;
            server->Release();
        }
        PropertyServer** link = &session->targets;
        while (*link && *link != server) link = &(*link)->next;
        if (*link && subclassRemoved)
        {
            *link = server->next;
            server->next = nullptr;
            server->Release(); // The target list's reference.
        }
        // Complete the disconnect transition before entering COM. A nested
        // destroy/Stop now observes an unlinked, disconnected registration.
        // Failed clears stay separately owned and retryable. Use the original
        // identity even if destruction nested inside the registration call.
        if (wasRegistered) ClearPending(server);
        server->Release();
    }

    LRESULT CALLBACK DestroySubclass(HWND hwnd, UINT message, WPARAM wParam,
        LPARAM lParam, UINT_PTR, DWORD_PTR reference)
    {
        if (message == WM_NCDESTROY)
        {
            auto* server = reinterpret_cast<PropertyServer*>(reference);
            server->AddRef();
            Log(server->session, L"destroyed", hwnd, {});
            Disconnect(server);
            const LRESULT result = DefSubclassProc(hwnd, message, wParam, lParam);
            // After final destruction no helper subclass can remain attached.
            if (server->subclassReference)
            {
                server->subclassReference = false;
                server->Release();
            }
            Disconnect(server); // Finalize a list entry after a failed removal.
            server->Release();
            return result;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    HRESULT Annotate(Session* session, HWND window) noexcept
    {
        if (!ReadCounter(&session->active)) return S_FALSE;
        for (PropertyServer* current = session->targets; current; current = current->next)
            if (current->hwnd == window) return S_FALSE;
        for (PendingRegistration* pending = session->registrations; pending; pending = pending->previous)
            if (pending->window == window) return S_FALSE;
        TabState state{};
        const HRESULT eligible = ReadTabState(session, window, &state);
        if (eligible != S_OK) return eligible;
        auto* server = new (std::nothrow) PropertyServer(session, window);
        if (!server) return E_OUTOFMEMORY;
        PendingRegistration registration(session, window);
        IAccPropServices* services = session->services;
        if (!services)
        {
            server->Release();
            return CO_E_OBJNOTCONNECTED;
        }
        services->AddRef();
        server->annotationServices = services;
        services->AddRef();
        HRESULT status = services->ComposeHwndIdentityString(window,
            static_cast<DWORD>(OBJID_CLIENT), CHILDID_SELF,
            &server->annotationIdentity, &server->annotationIdentitySize);
        if (FAILED(status) || !ReadCounter(&session->active))
        {
            services->Release();
            server->Release();
            return FAILED(status) ? status : CO_E_OBJNOTCONNECTED;
        }
        if (!SetWindowSubclass(window, DestroySubclass, reinterpret_cast<UINT_PTR>(server),
            reinterpret_cast<DWORD_PTR>(server)))
        {
            status = LastErrorResult();
            services->Release();
            server->Release();
            return status;
        }
        server->AddRef(); // The helper subclass owns an independent reference.
        server->subclassReference = true;
        server->next = session->targets;
        session->targets = server;
        server->AddRef(); // The target list owns an independent reference.
        const MSAAPROPID property = PROPID_ACC_VALUE;
        status = services->SetHwndPropServer(window,
            static_cast<DWORD>(OBJID_CLIENT), CHILDID_SELF, &property, 1,
            server, ANNO_THIS);
        services->Release();
        if (SUCCEEDED(status))
        {
            server->registered = true;
            InterlockedIncrement(&session->annotated);
            if (!ReadCounter(&server->connected) || !ReadCounter(&session->active))
                Disconnect(server);
            else
                Log(session, L"annotated", window, state);
        }
        else
        {
            Disconnect(server);
            RecordFailure(session, status);
            Log(session, L"annotate-failed", window, state, status);
        }
        server->Release();
        return status;
    }

    BOOL CALLBACK AnnotateChild(HWND window, LPARAM parameter)
    {
        auto* session = reinterpret_cast<Session*>(parameter);
        const HRESULT status = Annotate(session, window);
        if (FAILED(status)) RecordFailure(session, status);
        return ReadCounter(&session->active) ? TRUE : FALSE;
    }

    LRESULT CALLBACK ObserveCall(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0 && lParam)
        {
            const auto* call = reinterpret_cast<const CWPSTRUCT*>(lParam);
            if (call->message == WM_GETOBJECT
                && static_cast<LONG>(call->lParam) == OBJID_CLIENT)
            {
                Session* session = AcquireSession();
                if (session)
                {
                    const HRESULT status = Annotate(session, call->hwnd);
                    if (FAILED(status))
                    {
                        RecordFailure(session, status);
                        Log(session, L"discovery-failed", call->hwnd, {}, status);
                    }
                    session->Release();
                }
            }
        }
        return CallNextHookEx(nullptr, code, wParam, lParam);
    }

    HRESULT Cleanup(Session* session) noexcept
    {
        InterlockedExchange(&session->active, 0);
        if (session->hook)
        {
            if (!UnhookWindowsHookEx(session->hook)) RecordFailure(session, LastErrorResult());
            session->hook = nullptr;
        }
        while (session->targets)
        {
            PropertyServer* target = session->targets;
            Disconnect(target);
            if (session->targets == target)
            {
                // Keep a failed subclass removal owned and retryable. Never
                // free a callback's refData while Windows can still use it.
                return HRESULT_FROM_WIN32(ERROR_BUSY);
            }
        }
        while (session->pendingClears)
        {
            PropertyServer* pending = session->pendingClears;
            const HRESULT status = ClearPending(pending);
            if (session->pendingClears == pending) return status;
        }
        if (session->registrations)
        {
            // An outward registration call can still return success and need
            // clearing. Keep its session published until that call unwinds.
            return E_PENDING;
        }
        if (session->services)
        {
            session->services->Release();
            session->services = nullptr;
        }
        bool releaseGlobal = false;
        AcquireSRWLockExclusive(&g_sessionLock);
        if (g_session == session)
        {
            g_session = nullptr;
            releaseGlobal = true;
        }
        ReleaseSRWLockExclusive(&g_sessionLock);
        if (releaseGlobal) session->Release();
        return ReadCounter(&session->firstFailure);
    }
}

extern "C" BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
        return AfxInitExtensionModule(g_extension, instance);
    if (reason == DLL_PROCESS_DETACH) AfxTermExtensionModule(g_extension);
    return TRUE;
}

std::uint32_t __cdecl NanoCAD_AccessibilityGuardAbiVersion() noexcept
{
    return NanoCAD::Accessibility::AbiVersion;
}

HRESULT __cdecl NanoCAD_AccessibilityGuardStart(void* providerAddress,
    const wchar_t* logPath, Report* report) noexcept
{
    if (!ValidReport(report)) return E_INVALIDARG;
    WriteReport(report, nullptr, Phase::StartFailed, E_INVALIDARG);
    if (!providerAddress || !logPath || !*logPath) return E_INVALIDARG;
    Session* existing = AcquireSession();
    if (existing)
    {
        const HRESULT status = GetCurrentThreadId() == existing->ownerThread
            ? (ReadCounter(&existing->active) ? S_FALSE : HRESULT_FROM_WIN32(ERROR_BUSY))
            : RPC_E_WRONG_THREAD;
        WriteReport(report, existing, FAILED(status) ? Phase::StartFailed : Phase::Started, status);
        existing->Release();
        return status;
    }
    auto provider = reinterpret_cast<FrameProvider>(providerAddress);
    AFX_MODULE_STATE* module = nullptr;
    HWND frame = nullptr;
    HRESULT status = FrameContext(provider, GetCurrentThreadId(), GetCurrentProcessId(),
        nullptr, &module, &frame);
    if (FAILED(status))
    {
        WriteReport(report, nullptr, Phase::StartFailed, status);
        return status;
    }
    APTTYPE apartment{};
    APTTYPEQUALIFIER qualifier{};
    status = CoGetApartmentType(&apartment, &qualifier);
    if (SUCCEEDED(status) && apartment != APTTYPE_STA && apartment != APTTYPE_MAINSTA)
        status = RPC_E_WRONG_THREAD;
    if (FAILED(status))
    {
        WriteReport(report, nullptr, Phase::StartFailed, status);
        return status;
    }
    HMODULE pinned = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&NanoCAD_AccessibilityGuardStart), &pinned))
    {
        status = LastErrorResult();
        WriteReport(report, nullptr, Phase::StartFailed, status);
        return status;
    }
    auto* session = new (std::nothrow) Session;
    if (!session)
    {
        WriteReport(report, nullptr, Phase::StartFailed, E_OUTOFMEMORY);
        return E_OUTOFMEMORY;
    }
    session->ownerThread = GetCurrentThreadId();
    session->process = GetCurrentProcessId();
    session->frameWindow = frame;
    session->provider = provider;
    HMODULE mfc = GetModuleHandleA(NanoCAD::Accessibility::Build::MfcImportDll);
    if (mfc)
        session->baseValueGetter = reinterpret_cast<std::uintptr_t>(GetProcAddress(mfc,
            MAKEINTRESOURCEA(NanoCAD::Accessibility::Build::BaseValueOrdinal)));
    session->baseValueGetter = ResolveImportThunk(session->baseValueGetter);
    if (!session->baseValueGetter || !ValueDispatchOffset(&session->valueDispatchOffset))
    {
        WriteReport(report, session, Phase::StartFailed, E_NOTIMPL);
        session->Release();
        return E_NOTIMPL;
    }
    session->log = CreateFileW(logPath, FILE_APPEND_DATA,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (session->log == INVALID_HANDLE_VALUE) status = LastErrorResult();
    else status = CoCreateInstance(CLSID_AccPropServices, nullptr, CLSCTX_INPROC_SERVER,
        __uuidof(IAccPropServices), reinterpret_cast<void**>(&session->services));
    if (SUCCEEDED(status))
    {
        InterlockedExchange(&session->active, 1);
        AcquireSRWLockExclusive(&g_sessionLock);
        g_session = session;
        session->AddRef();
        ReleaseSRWLockExclusive(&g_sessionLock);
        session->hook = SetWindowsHookExW(WH_CALLWNDPROC, ObserveCall, pinned, session->ownerThread);
        if (!session->hook) status = LastErrorResult();
        else
        {
            EnumChildWindows(frame, AnnotateChild, reinterpret_cast<LPARAM>(session));
            status = ReadCounter(&session->firstFailure);
            if (SUCCEEDED(status) && !ReadCounter(&session->active))
                status = HRESULT_FROM_WIN32(ERROR_CANCELLED);
        }
    }
    if (FAILED(status))
    {
        RecordFailure(session, status);
        Log(session, L"start-failed", frame, {}, status);
        Cleanup(session);
        WriteReport(report, session, Phase::StartFailed, status);
    }
    else
    {
        Log(session, L"started", frame, {});
        WriteReport(report, session, Phase::Started, S_OK);
    }
    session->Release();
    return status;
}

HRESULT __cdecl NanoCAD_AccessibilityGuardStop(Report* report) noexcept
{
    if (!ValidReport(report)) return E_INVALIDARG;
    Session* session = AcquireSession();
    if (!session)
    {
        WriteReport(report, nullptr, Phase::Stopped, S_FALSE);
        return S_FALSE;
    }
    if (GetCurrentThreadId() != session->ownerThread)
    {
        InterlockedIncrement(&session->wrongThread);
        WriteReport(report, session, Phase::StopFailed, RPC_E_WRONG_THREAD);
        session->Release();
        return RPC_E_WRONG_THREAD;
    }
    const HRESULT status = Cleanup(session);
    Log(session, L"stopped", session->frameWindow, {}, status);
    WriteReport(report, session, FAILED(status) ? Phase::StopFailed : Phase::Stopped, status);
    session->Release();
    return status;
}
