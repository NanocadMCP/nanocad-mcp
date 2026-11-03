// Exact body transparency preservation through the public NRX database API.
// This is a separate translation unit from the MultiCAD interface adapter.
// The owning WriteJob calls these exports on the active drawing thread.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define HOST_NO_MFC
#define _MCS_CORE_ONLY
#include <windows.h>
#include <cstdint>
#include <string>
#include <vector>
#include <algorithm>
#include "nrxdbgate/ncheadersDb.h"

namespace
{
    HRESULT PathError()
    {
        const DWORD error = GetLastError();
        return error ? HRESULT_FROM_WIN32(error) : E_FAIL;
    }

    HRESULT CanonicalPath(const wchar_t* text, std::wstring& result)
    {
        if (!text || !*text) return E_INVALIDARG;
        // An unnamed/relative drawing does not establish a database identity.
        const bool drive = text[0] && text[1] == L':' &&
            (text[2] == L'\\' || text[2] == L'/');
        const bool unc = text[0] == L'\\' && text[1] == L'\\';
        if (!drive && !unc) return E_INVALIDARG;
        const DWORD capacity = GetFullPathNameW(text, 0, nullptr, nullptr);
        if (!capacity) return PathError();
        std::vector<wchar_t> buffer(capacity);
        wchar_t* file = nullptr;
        const DWORD count = GetFullPathNameW(text, capacity, buffer.data(), &file);
        if (!count) return PathError();
        if (count >= capacity || !file || !*file)
            return HRESULT_FROM_WIN32(ERROR_INVALID_NAME);
        result.assign(buffer.data(), count);
        std::replace(result.begin(), result.end(), L'/', L'\\');
        return S_OK;
    }

    HRESULT ResolveBody(const wchar_t* drawingPath, std::uint64_t rawHandle,
                        NcDbDatabase*& database, NcDbObjectId& id)
    {
        if (!rawHandle) return E_INVALIDARG;
        std::wstring expected;
        HRESULT status = CanonicalPath(drawingPath, expected);
        if (FAILED(status)) return status;
        auto* host = ncdbHostApplicationServices();
        if (!host || !(database = host->workingDatabase())) return E_UNEXPECTED;
        const NCHAR* filename = nullptr;
        if (database->getFilename(filename) != Nano::eOk || !filename || !*filename)
            return HRESULT_FROM_WIN32(ERROR_INVALID_NAME);
        std::wstring actual;
        status = CanonicalPath(filename, actual);
        if (FAILED(status)) return status;
        if (CompareStringOrdinal(expected.c_str(), -1, actual.c_str(), -1, TRUE) != CSTR_EQUAL)
            return E_INVALIDARG;
        const NRX::UInt64 nativeHandle = static_cast<NRX::UInt64>(rawHandle);
        const NcDbHandle handle(nativeHandle);
        if (database->getNcDbObjectId(id, false, handle) != Nano::eOk ||
            id.isNull() || id.database() != database || id.isErased() ||
            id.isEffectivelyErased() || id.handle() != handle)
            return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        return S_OK;
    }

    HRESULT RequireBody(NcDb3dSolid* body, NcDbDatabase* database,
                        NcDbObjectId id, std::uint64_t rawHandle)
    {
        if (!body || body->isA() != NcDb3dSolid::desc()) return E_NOINTERFACE;
        const auto* symbols = ncdbSymUtil();
        auto* host = ncdbHostApplicationServices();
        if (!symbols || !database || !host || host->workingDatabase() != database)
            return E_UNEXPECTED;
        const NcDbObjectId model = symbols->blockModelSpaceId(database);
        NcDbHandle actual;
        body->getNcDbHandle(actual);
        if (model.isNull() || model.database() != database ||
            body->database() != database || body->objectId() != id ||
            body->ownerId() != model || body->isErased() ||
            static_cast<NRX::UInt64>(actual) != static_cast<NRX::UInt64>(rawHandle))
            return E_INVALIDARG;
        return S_OK;
    }
}

extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_ReadBodyTransparency445(const wchar_t* drawingPath, std::uint64_t rawHandle,
                               std::uint32_t* raw)
{
    if (!raw) return E_INVALIDARG;
    *raw = 0;
    try
    {
        NcDbDatabase* database = nullptr;
        NcDbObjectId id;
        HRESULT status = ResolveBody(drawingPath, rawHandle, database, id);
        if (FAILED(status)) return status;
        NcDbObjectPointer<NcDb3dSolid> body(id, NcDb::kForRead, false);
        if (body.openStatus() != Nano::eOk) return E_FAIL;
        status = RequireBody(body.object(), database, id, rawHandle);
        if (FAILED(status)) return status;
        const NcCmTransparency value = body->transparency();
        if (value.isInvalid()) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        *raw = value.serializeOut();
        return S_OK;
    }
    catch (...) { return E_UNEXPECTED; }
}

extern "C" __declspec(dllexport) HRESULT __cdecl
NanoCAD_RestoreBodyTransparency445(const wchar_t* drawingPath, std::uint64_t rawHandle,
                                  std::uint32_t raw)
{
    try
    {
        NcCmTransparency requested;
        requested.serializeIn(static_cast<NRX::UInt32>(raw));
        if (requested.isInvalid() || requested.serializeOut() != raw)
            return E_INVALIDARG;
        NcDbDatabase* database = nullptr;
        NcDbObjectId id;
        HRESULT status = ResolveBody(drawingPath, rawHandle, database, id);
        if (FAILED(status)) return status;
        NcDbObjectPointer<NcDb3dSolid> body(id, NcDb::kForWrite, false);
        if (body.openStatus() != Nano::eOk) return E_FAIL;
        status = RequireBody(body.object(), database, id, rawHandle);
        if (FAILED(status)) return status;
        // Public SDK's subents=false changes only the body setting, retaining
        // face overrides. No face inventory, private layout or vtable is used.
        if (body->setTransparency(requested, false) != Nano::eOk) return E_FAIL;
        const NcCmTransparency actual = body->transparency();
        if (actual.isInvalid() || actual.serializeOut() != raw)
            return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        return S_OK;
    }
    catch (...) { return E_UNEXPECTED; }
}
