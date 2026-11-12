// Copyright 2026 Andrey Juliovich Camejo Severinov. MIT License.
// Owning direct-solid replacement through public NRX APIs.
// The existing WriteJob calls this export on the active drawing thread.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define HOST_NO_MFC
#define _MCS_CORE_ONLY
#include <windows.h>
#include <cstdint>
#include <climits>
#include <cmath>
#include "nrxdbgate/ncheadersDb.h"

namespace
{
    constexpr std::uint32_t Abi = 1;
    constexpr std::int32_t NotObserved = INT32_MIN;
    enum Phase : std::uint32_t
    {
        Entry = 1, Context = 2, ResolveIds = 3, OpenSource = 4,
        OpenTarget = 5, Clone = 6, SetProperties = 7, BeginWrite = 8,
        HandOver = 9, ObserveAfter = 10, Cleanup = 11, Returned = 12,
        DonorGeometry = 13, DonorMass = 14, DonorExtents = 15
    };
    enum CleanupKind : std::uint32_t { None = 0, DetachedDelete = 1, ResidentClose = 2 };

    // This is our own versioned transport structure, not a vendor object layout.
#pragma pack(push, 8)
    struct Report
    {
        std::uint32_t size, version, phase, failurePhase;
        std::uint32_t contextChecked, nativeTransactionCount, cloneCreated, cloneQuarantined;
        std::uint32_t beginWriteCalled, beginWriteReturned, handoverStarted, handoverReturned;
        std::uint32_t cleanupEntered, cleanupReturned, cleanupSucceeded, postHandoverObserved;
        std::int32_t sourceOpenStatus, targetOpenStatus, propertiesStatus, beginWriteStatus;
        std::int32_t handoverStatus, operationStatus, donorCleanupStatus, targetCleanupStatus;
        std::int32_t sourceCleanupStatus;
        std::uint32_t donorCleanupKind, targetCleanupKind, sourceCleanupKind;
        std::uint32_t donorCleanupCompleted, targetCleanupCompleted, sourceCleanupCompleted;
        std::uint64_t targetInput, sourceInput, modelOwner;
        std::uint64_t targetCurrentId, targetCurrentOwner, donorCurrentId, donorCurrentOwner;
        std::uint64_t sourceCurrentId, sourceCurrentOwner;
    };
#pragma pack(pop)
    static_assert(sizeof(Report) == 200, "Native solid-replacement transport ABI changed.");
    using BeginWriteCallback = HRESULT (__cdecl*)(void*);

    std::uint64_t Handle(NcDbObjectId id)
    {
        return id.isNull() ? 0 : static_cast<std::uint64_t>(static_cast<NRX::UInt64>(id.handle()));
    }

    bool ExactSolid(const NcRxObject* value)
    {
        auto* expected = NcDb3dSolid::desc();
        auto* actual = value ? value->isA() : nullptr;
        return expected && actual && expected->native() && actual->native() == expected->native();
    }

    bool HasPersistentReactors(const NcDbObject* value)
    {
        const auto* reactors = value ? value->reactors() : nullptr;
        if (!reactors) return false;
        for (int index = 0; index < reactors->length(); ++index)
            if (ncdbIsPersistentReactor(reactors->at(index))) return true;
        return false;
    }

    bool FiniteWcsPoint(const NcGePoint3d& point)
    {
        // Exact existing CadSolidEditor.CheckGeometry coordinate admission.
        return std::isfinite(point.x) && std::isfinite(point.y) && std::isfinite(point.z) &&
            std::abs(point.x) <= 1e9 && std::abs(point.y) <= 1e9 && std::abs(point.z) <= 1e9;
    }

    HRESULT CheckDonorGeometry(const NcDb3dSolid* value, Report& report)
    {
        // Public SDK database.h:10296-10302 / :4513. This is pre-write
        // validity parity, not a source/clone volume or interpolation oracle.
        report.phase = DonorGeometry;
        if (!value || value->isNull()) return E_INVALIDARG;
        double volume = 0;
        NcGePoint3d centroid;
        double moments[3]{}, products[3]{}, principalMoments[3]{}, gyration[3]{};
        NcGeVector3d principalAxes[3];
        NcDbExtents massExtents;
        report.phase = DonorMass;
        if (value->getMassProp(volume, centroid, moments, products, principalMoments,
            principalAxes, gyration, massExtents) != Nano::eOk) return E_FAIL;
        // Same finite-positive volume floor as CadSolidEditor.CheckGeometry.
        if (!std::isfinite(volume) || volume <= 1e-12 || !FiniteWcsPoint(centroid))
            return E_INVALIDARG;
        NcDbExtents extents;
        report.phase = DonorExtents;
        if (value->getGeomExtents(extents) != Nano::eOk) return E_FAIL;
        return FiniteWcsPoint(extents.minPoint()) && FiniteWcsPoint(extents.maxPoint()) ? S_OK : E_INVALIDARG;
    }

    HRESULT CurrentDatabase(void* expected, NcDbDatabase*& database)
    {
        if (!expected) return E_INVALIDARG;
        auto* manager = ncDocManager;
        auto* document = manager ? manager->curDocument() : nullptr;
        auto* host = ncdbHostApplicationServices();
        auto* working = host ? host->workingDatabase() : nullptr;
        database = document ? document->database() : nullptr;
        // Compare the actual underlying OdDbDatabase identities. No NRX wrapper
        // is constructed from a Teigha pointer; unnamed drawings remain valid.
        if (!database || !working || !database->native() ||
            static_cast<void*>(database->native()) != expected ||
            static_cast<void*>(working->native()) != expected)
            return E_INVALIDARG;
        return S_OK;
    }

    HRESULT Resolve(NcDbDatabase* database, std::uint64_t raw, NcDbObjectId& id)
    {
        if (!database || !raw) return E_INVALIDARG;
        const NcDbHandle handle(static_cast<NRX::UInt64>(raw));
        const auto status = database->getNcDbObjectId(id, false, handle);
        auto* ownerDatabase = id.isNull() ? nullptr : id.database();
        if (status != Nano::eOk || id.isNull() || !ownerDatabase || ownerDatabase->native() != database->native() ||
            id.isErased() || id.isEffectivelyErased() || id.handle() != handle)
            return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        return S_OK;
    }

    HRESULT RequireRoot(NcDb3dSolid* value, NcDbDatabase* database,
                        NcDbObjectId id, NcDbObjectId model)
    {
        auto* actualDatabase = value ? value->database() : nullptr;
        return value && ExactSolid(value) && actualDatabase && actualDatabase->native() == database->native() &&
            value->objectId() == id && value->ownerId() == model &&
            !value->isErased() && value->isReadEnabled() ? S_OK : E_INVALIDARG;
    }

    void Observe(Report& report, NcDb3dSolid* target, NcDb3dSolid* donor, NcDb3dSolid* source)
    {
        if (target) { report.targetCurrentId = Handle(target->objectId()); report.targetCurrentOwner = Handle(target->ownerId()); }
        if (donor) { report.donorCurrentId = Handle(donor->objectId()); report.donorCurrentOwner = Handle(donor->ownerId()); }
        if (source) { report.sourceCurrentId = Handle(source->objectId()); report.sourceCurrentOwner = Handle(source->ownerId()); }
    }

    // Public objptr.h::close() discards its internal native close result. Keep
    // ownership in the SDK pointer until cleanup, then release exactly once and
    // apply its documented current-ID closeInternal policy with the real status.
    // A failed close is never retried, deleted or returned to another owner.
    bool CleanupOwned(NcDbObjectPointer<NcDb3dSolid>& owner, std::uint32_t& kind,
                      std::int32_t& status, std::uint32_t& completed)
    {
        if (owner.openStatus() != Nano::eOk) return true;
        NcDb3dSolid* value = nullptr;
        const auto releaseStatus = owner.release(value);
        if (releaseStatus != Nano::eOk || !value)
        {
            status = static_cast<std::int32_t>(releaseStatus);
            return false;
        }
        try
        {
            if (value->objectId().isNull())
            {
                kind = DetachedDelete;
                delete value;
                completed = 1; // Delete returned; no native close status exists.
                return true;
            }
            kind = ResidentClose;
            const auto closeStatus = value->close();
            status = static_cast<std::int32_t>(closeStatus);
            completed = 1;
            return closeStatus == Nano::eOk;
        }
        catch (...) { return false; } // Status remains unobserved on an exception.
    }

    HRESULT Operate(void* expectedDatabase, std::uint64_t targetRaw, std::uint64_t sourceRaw,
                    BeginWriteCallback beginWrite, void* callbackState, Report& report,
                    NcDbObjectPointer<NcDb3dSolid>& target,
                    NcDbObjectPointer<NcDb3dSolid>& donor,
                    NcDbObjectPointer<NcDb3dSolid>& source)
    {
        report.phase = Context;
        NcDbDatabase* database = nullptr;
        HRESULT status = CurrentDatabase(expectedDatabase, database);
        if (FAILED(status)) return status;
        auto* document = ncDocManager->curDocument();
        auto* transactions = document ? document->transactionManager() : nullptr;
        if (!transactions) return E_UNEXPECTED;
        const int active = transactions->numActiveTransactions();
        if (active < 0) return E_UNEXPECTED;
        report.nativeTransactionCount = static_cast<std::uint32_t>(active);
        if (active != 0) return E_UNEXPECTED;
        report.contextChecked = 1;
        report.phase = ResolveIds;
        NcDbObjectId targetId, sourceId;
        if (targetRaw == sourceRaw) return E_INVALIDARG;
        status = Resolve(database, targetRaw, targetId);
        if (FAILED(status)) return status;
        status = Resolve(database, sourceRaw, sourceId);
        if (FAILED(status)) return status;
        auto* symbols = ncdbSymUtil();
        const NcDbObjectId model = symbols ? symbols->blockModelSpaceId(database) : NcDbObjectId();
        auto* modelDatabase = model.isNull() ? nullptr : model.database();
        if (model.isNull() || !modelDatabase || modelDatabase->native() != database->native()) return E_INVALIDARG;
        report.modelOwner = Handle(model);

        report.phase = OpenSource;
        report.sourceOpenStatus = static_cast<std::int32_t>(source.open(sourceId, NcDb::kForRead, false));
        if (report.sourceOpenStatus != Nano::eOk) return E_FAIL;
        status = RequireRoot(source.object(), database, sourceId, model);
        if (FAILED(status)) return status;
        report.phase = OpenTarget;
        report.targetOpenStatus = static_cast<std::int32_t>(target.open(targetId, NcDb::kForWrite, false));
        if (report.targetOpenStatus != Nano::eOk) return E_FAIL;
        status = RequireRoot(target.object(), database, targetId, model);
        if (FAILED(status) || !target->isWriteEnabled()) return E_INVALIDARG;

        report.phase = Clone;
        NcRxObject* raw = source->clone();
        if (!raw) return E_FAIL;
        // Reject aliases before an owning pointer is allowed to acquire anything.
        if (raw == source.object() || raw == target.object() || !ExactSolid(raw) ||
            !raw->native() || raw->native() == source.object()->NcRxObject::native() ||
            raw->native() == target.object()->NcRxObject::native())
        {
            report.cloneQuarantined = 1;
            return E_NOINTERFACE;
        }
        // The public SDK cast is used only after exact native descriptor proof.
        // There is no reinterpretation of a managed/native or private pointer.
        auto* candidate = NcDb3dSolid::cast(raw);
        if (!candidate || !candidate->objectId().isNull() || !candidate->ownerId().isNull())
        {
            report.cloneQuarantined = 1;
            return E_INVALIDARG;
        }
        auto* adopted = candidate;
        if (donor.acquire(adopted) != Nano::eOk || adopted) return E_UNEXPECTED;
        report.cloneCreated = 1;
        if (!donor->extensionDictionary().isNull() || donor->hasFields() || HasPersistentReactors(donor.object())) return E_INVALIDARG;
        status = CheckDonorGeometry(donor.object(), report);
        if (FAILED(status)) return status;
        report.phase = SetProperties;
        report.propertiesStatus = static_cast<std::int32_t>(donor->setPropertiesFrom(target.object(), true));
        if (report.propertiesStatus != Nano::eOk) return E_FAIL;
        // The managed writer already captured the full foreign-data/history and
        // documentation plan. Its callback rechecks admission before started.
        status = CurrentDatabase(expectedDatabase, database);
        if (FAILED(status)) return status;
        if (FAILED(RequireRoot(target.object(), database, targetId, model)) ||
            FAILED(RequireRoot(source.object(), database, sourceId, model))) return E_INVALIDARG;
        report.phase = BeginWrite;
        report.beginWriteCalled = 1;
        report.beginWriteStatus = beginWrite(callbackState);
        report.beginWriteReturned = 1;
        if (FAILED(report.beginWriteStatus)) return report.beginWriteStatus;
        report.phase = HandOver;
        report.handoverStarted = 1;
        report.handoverStatus = static_cast<std::int32_t>(target->handOverTo(donor.object(), true, true));
        report.handoverReturned = 1;
        report.phase = ObserveAfter;
        Observe(report, target.object(), donor.object(), source.object());
        report.postHandoverObserved = 1;
        // The pinned installed NRX wrapper returns eObjectToBeDeleted after
        // its underlying void OdDbObject::handOverTo returns normally. Retain
        // that raw lifecycle status; it is not a general nonzero-success rule.
        // See artifacts/direct3d-o-native-prototype-v2/handover-return-sdk-binary.json for the exact SDK/binary evidence.
        const bool returnedLifecycle = report.handoverStatus == Nano::eObjectToBeDeleted;
        const bool observedExchange = target->objectId().isNull() && target->ownerId().isNull() &&
            donor->objectId() == targetId && donor->ownerId() == model &&
            source->objectId() == sourceId && source->ownerId() == model;
        return returnedLifecycle && observedExchange ? S_OK : E_FAIL;
    }
}

extern "C" __declspec(dllexport) std::uint32_t __cdecl NanoCAD_SolidReplacement409AbiVersion()
{
    return Abi;
}

extern "C" __declspec(dllexport) HRESULT __cdecl NanoCAD_SolidReplacement409(
    void* expectedDatabase, std::uint64_t targetHandle, std::uint64_t sourceHandle,
    BeginWriteCallback beginWrite, void* callbackState, Report* output)
{
    if (!output || output->size != sizeof(Report) || !beginWrite) return E_INVALIDARG;
    Report report{};
    report.size = sizeof(Report); report.version = Abi; report.phase = Entry;
    report.targetInput = targetHandle; report.sourceInput = sourceHandle;
    report.sourceOpenStatus = report.targetOpenStatus = report.propertiesStatus = NotObserved;
    report.beginWriteStatus = report.handoverStatus = report.operationStatus = NotObserved;
    report.donorCleanupStatus = report.targetCleanupStatus = report.sourceCleanupStatus = NotObserved;
    report.nativeTransactionCount = UINT32_MAX;
    NcDbObjectPointer<NcDb3dSolid> source, target, donor;
    HRESULT status = E_UNEXPECTED;
    try { status = Operate(expectedDatabase, targetHandle, sourceHandle, beginWrite, callbackState, report, target, donor, source); }
    catch (...) { status = E_UNEXPECTED; }
    report.operationStatus = status;
    if (FAILED(status)) report.failurePhase = report.phase;
    report.phase = Cleanup; report.cleanupEntered = 1;
    // Reverse acquisition order; all owners are emptied before destruction.
    const bool donorClean = CleanupOwned(donor, report.donorCleanupKind, report.donorCleanupStatus, report.donorCleanupCompleted);
    const bool targetClean = CleanupOwned(target, report.targetCleanupKind, report.targetCleanupStatus, report.targetCleanupCompleted);
    const bool sourceClean = CleanupOwned(source, report.sourceCleanupKind, report.sourceCleanupStatus, report.sourceCleanupCompleted);
    report.cleanupReturned = 1;
    report.cleanupSucceeded = donorClean && targetClean && sourceClean && !report.cloneQuarantined ? 1 : 0;
    if (!report.cleanupSucceeded && SUCCEEDED(status)) { status = E_FAIL; report.failurePhase = Cleanup; }
    report.phase = Returned;
    *output = report;
    return status;
}
