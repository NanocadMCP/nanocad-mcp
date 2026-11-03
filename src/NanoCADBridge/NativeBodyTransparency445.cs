using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// The managed Entity.Transparency setter applies to subentities as well. This
// adapter uses the public NRX setter with subents=false, preserving face overrides.
// Handles are always resolved through the exact current native body identity.
internal static class NativeBodyTransparency445
{
    internal sealed record Witness(McObjectId BodyId,McObjectId OwnerId,string DrawingPath,ulong OriginalHandle,uint Raw);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AbiVersion();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
    private delegate int ReadNative([MarshalAs(UnmanagedType.LPWStr)]string path,ulong handle,out uint raw);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
    private delegate int RestoreNative([MarshalAs(UnmanagedType.LPWStr)]string path,ulong handle,uint raw);
    private sealed record Api(IntPtr Library,ReadNative Read,RestoreNative Restore);
    private static readonly object Sync=new();
    private static Api? loaded;
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    static DomainException Unavailable(string message)=>new("CAPABILITY_UNAVAILABLE",message);
    static Api Load()
    {
        lock(Sync)
        {
            if(loaded!=null)return loaded;
            IntPtr library=IntPtr.Zero;
            try
            {
                string directory=Path.GetDirectoryName(typeof(NativeBodyTransparency445).Assembly.Location)
                    ??throw Unavailable("The native body-transparency adapter's bridge directory is unavailable.");
                string path=Path.Combine(directory,"NanoCAD.NativeModelHistory421.dll");
                if(!File.Exists(path))throw Unavailable("The native body-transparency adapter is missing beside the bridge.");
                library=NativeLibrary.Load(path,typeof(NativeBodyTransparency445).Assembly,
                    DllImportSearchPath.UseDllDirectoryForDependencies|DllImportSearchPath.System32|DllImportSearchPath.ApplicationDirectory);
                var version=Marshal.GetDelegateForFunctionPointer<AbiVersion>(NativeLibrary.GetExport(library,"NanoCAD_NativeModelHistoryAbiVersion"));
                if(version()!=1)throw Unavailable("The native body-transparency adapter has an incompatible ABI.");
                loaded=new Api(library,
                    Marshal.GetDelegateForFunctionPointer<ReadNative>(NativeLibrary.GetExport(library,"NanoCAD_ReadBodyTransparency445")),
                    Marshal.GetDelegateForFunctionPointer<RestoreNative>(NativeLibrary.GetExport(library,"NanoCAD_RestoreBodyTransparency445")));
                return loaded;
            }
            catch(Exception error)when(error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {throw Unavailable("The public native body-transparency adapter or its installed CAD dependency could not be loaded.");}
            finally{if(loaded==null&&library!=IntPtr.Zero)NativeLibrary.Free(library);}
        }
    }
    static void Status(int status,string phase,Witness witness,ulong handle)
    {
        if(status!=0)throw new DomainException("MODEL_APPEARANCE_UNAVAILABLE","The exact native body-transparency observation or preservation failed.",false,
            new{phase,native_hresult="0x"+unchecked((uint)status).ToString("X8"),body_id=witness.BodyId.ToString(),
                owner_document_id=witness.OwnerId.ToString(),representation_handle=handle.ToString("X")});
    }
    static uint Read(Witness witness,ulong handle)
    {int status=Load().Read(witness.DrawingPath,handle,out uint raw);Status(status,"read_body_transparency",witness,handle);return raw;}
    static (Mc3dSolid Body,Db.ObjectId Representation,ulong Handle) Resolve(Witness witness,DocumentState state,Db.Transaction tx,Action cancel)
    {
        cancel();var body=witness.BodyId.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==witness.BodyId&&body.DbEntity.Document?.ID==witness.OwnerId,
            "OBJECT_IDENTITY_MISMATCH","Transparency preservation requires the exact retained body and native owner.");
        Need(state.Document.IsNamedDrawing&&string.Equals(Path.GetFullPath(state.Document.Name),witness.DrawingPath,StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH","The original body-transparency witness belongs to another named drawing.");
        var representation=ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel);
        var entity=tx.GetObject(representation,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(entity!=null&&entity.GetType()==typeof(Db.Solid3d)&&entity.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),
            "OBJECT_IDENTITY_MISMATCH","The current transparency target is not the exact top-level native solid representation.");
        ulong handle=unchecked((ulong)representation.Handle.Value);
        Need(handle!=0,"OBJECT_IDENTITY_MISMATCH","The current native body representation has no real handle.");
        return(body,representation,handle);
    }
    static JsonElement UnselectedProperties(Db.Solid3d entity)
    {
        var properties=JsonNode.Parse(JsonSerializer.Serialize(EntityAdapter.StoredProperties(entity),Wire.Json))!.AsObject();
        properties.Remove("transparency");
        return Wire.Element(new{owner=entity.OwnerId.Handle.ToString(),layer=entity.LayerId.Handle.ToString(),linetype=entity.LinetypeId.Handle.ToString(),properties});
    }
    // Called by the existing full sketch snapshot before the expression setter.
    // Reading native transparency is compatible with the caller's read transaction.
    internal static Witness Capture(Mc3dSolid body,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var owner=body.DbEntity.Document;
        Need(owner!=null&&!owner.ID.IsNull&&state.Document.IsNamedDrawing,"DOCUMENT_CONTEXT_MISMATCH","Raw body transparency requires its actual named native owner.");
        var initial=new Witness(body.ID,owner.ID,Path.GetFullPath(state.Document.Name),0,0);
        var current=Resolve(initial,state,tx,cancel);
        return initial with{OriginalHandle=current.Handle,Raw=Read(initial,current.Handle)};
    }
    internal static void Preserve(Witness witness,DocumentState state,Action cancel)
    {
        Db.ObjectId representation;ulong handle;JsonElement unselected;
        using(var tx=state.Document.Database.TransactionManager.StartTransaction())
        {
            var current=Resolve(witness,state,tx,cancel);representation=current.Representation;handle=current.Handle;
            unselected=UnselectedProperties((Db.Solid3d)tx.GetObject(representation,Db.OpenMode.ForRead));
        }
        // No managed read transaction may remain open when the NRX object is
        // opened for write. Only the current body's transparency is restored.
        cancel();if(Read(witness,handle)!=witness.Raw)
            Status(Load().Restore(witness.DrawingPath,handle,witness.Raw),"restore_body_transparency_without_subentities",witness,handle);
        Need(Read(witness,handle)==witness.Raw,"MODEL_APPEARANCE_CHANGED","The body did not retain its original raw native transparency.");
        using(var tx=state.Document.Database.TransactionManager.StartTransaction())
        {
            var current=Resolve(witness,state,tx,cancel);
            Need(current.Representation==representation&&current.Handle==handle,"OBJECT_IDENTITY_MISMATCH","Body-transparency preservation changed its actual representation identity.");
            Need(ModelParameterSnapshot.Equivalent(unselected,UnselectedProperties((Db.Solid3d)tx.GetObject(representation,Db.OpenMode.ForRead))),
                "MODEL_APPEARANCE_CHANGED","Body-transparency preservation changed another retained appearance or owner property.");
        }
    }
    // Matching command-end callback: verification only, never a delayed write.
    internal static void Verify(Witness witness,DocumentState state,Action cancel)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();
        var current=Resolve(witness,state,tx,cancel);
        Need(Read(witness,current.Handle)==witness.Raw,"MODEL_APPEARANCE_CHANGED","The completed native editor update did not retain the original raw body transparency.");
    }
}
