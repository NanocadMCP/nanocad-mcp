using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;
using Rx=Teigha.Runtime;

namespace NanoCADBridge;

// The installed SDK factory invokes the protected (IntPtr,bool) constructor.
// It does not clone, repost, change owner or take ownership of the resident.
internal static class SectionNativeBinding357
{
    internal sealed class Binding<T>:IDisposable where T:Db.DBObject
    {
        readonly Db.DBObject source;readonly bool borrowed;readonly IntPtr pointer;
        internal T Value{get;}
        internal Binding(Db.DBObject source,T value,bool borrowed){this.source=source;Value=value;this.borrowed=borrowed;pointer=source.UnmanagedObject;}
        public void Dispose()
        {
            try
            {
                if(borrowed)
                {
                    if(Value.AutoDelete||Value.UnmanagedObject!=pointer)Reject(Value,"Borrowed Section wrapper ownership/pointer changed; unsafe wrapper was quarantined without disposal.");
                    Value.Dispose();
                }
            }
            finally{GC.KeepAlive(source);}
        }
    }
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool value,string message)
    {if(!value)throw new DomainException("SECTION_NATIVE_BINDING_UNAVAILABLE",message);}
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    static void Reject(Rx.DisposableWrapper? value,string message)
    {
        // Only an unproved factory/borrowed result is quarantined: allowing an
        // owning alias finalizer to run could delete the retained native object.
        // No flag is changed and no native deletion/disposal is attempted.
        if(value!=null)GC.SuppressFinalize(value);
        throw new DomainException("SECTION_NATIVE_BINDING_UNAVAILABLE",message);
    }
    // The installed Section wrapper calls the OdDbSection base interface. A
    // native subclass is compatible only when the SDK proves its inheritance
    // from that descriptor; Nc/Ac names or the managed wrapper type cannot
    // prove this. This also covers derived resident SectionSettings objects.
    static bool Compatible(Db.DBObject value,Type type,out object observation,out IntPtr actualClass)
    {
        Rx.RXClass? expected=null,actual=null;actualClass=IntPtr.Zero;observation=new{expected_managed_type=type.FullName,native_class_observed=false};
        try
        {
            expected=Rx.RXObject.GetClass(type);actual=value.GetRXClass();
            if(expected!=null&&expected.AutoDelete)Reject(expected,"The expected native class descriptor is not a proved borrowed wrapper.");
            if(actual!=null&&actual.AutoDelete)Reject(actual,"The actual native class descriptor is not a proved borrowed wrapper.");
            bool expectedLive=expected!=null&&expected.UnmanagedObject!=IntPtr.Zero,actualLive=actual!=null&&actual.UnmanagedObject!=IntPtr.Zero;
            bool same=expectedLive&&actualLive&&actual!.UnmanagedObject==expected!.UnmanagedObject;
            bool derived=expectedLive&&actualLive&&!same&&actual!.IsDerivedFrom(expected!);
            if(actualLive)actualClass=actual!.UnmanagedObject;
            // Names are diagnostics, never the admission/cast criterion. They
            // distinguish missing descriptors/custom native representations
            // from database/identity failures without substituting a name alias
            // for the native inheritance check.
            observation=new{expected_managed_type=type.FullName,native_class_observed=expectedLive&&actualLive,
                expected_descriptor_available=expectedLive,actual_descriptor_available=actualLive,exact_descriptor=same,
                derived_from_adapter=derived,compatible_descriptor=same||derived,
                expected_name=expectedLive?expected!.Name:null,expected_dxf_name=expectedLive?expected!.DxfName:null,
                actual_name=actualLive?actual!.Name:null,actual_dxf_name=actualLive?actual!.DxfName:null};
            return same||derived;
        }
        finally
        {
            // PE/IL proves non-owning descriptor wrappers, not a managed cache
            // lifetime. Dispose each proved borrowed wrapper once; native class
            // identity survives. Unproved ownership is quarantined, never freed.
            var descriptors=new[]{expected,actual}.OfType<Rx.RXClass>().Distinct(ReferenceEqualityComparer.Instance);
            foreach(var descriptor in descriptors)if(descriptor is Rx.RXClass item)
            {if(!item.AutoDelete)item.Dispose();else GC.SuppressFinalize(item);}
            GC.KeepAlive(value);
        }
    }
    internal static bool IsSection(Db.DBObject value,Action cancel){cancel();return value.UnmanagedObject!=IntPtr.Zero&&Compatible(value,typeof(Db.Section),out _,out _);}
    static Binding<T> Bind<T>(Db.DBObject source,Db.Database database,Action cancel) where T:Db.DBObject
    {
        cancel();var pointer=source.UnmanagedObject;var id=source.ObjectId;var owner=source.OwnerId;
        bool identity=pointer!=IntPtr.Zero&&!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
        bool databaseMatches=database.UnmanagedObject!=IntPtr.Zero&&source.Database?.UnmanagedObject==database.UnmanagedObject;
        object classDetails=new{native_class_observed=false};
        IntPtr nativeClass=IntPtr.Zero;
        bool compatible=pointer!=IntPtr.Zero&&Compatible(source,typeof(T),out classDetails,out nativeClass);
        if(!identity||!databaseMatches||!compatible)
        {
            object details=new{managed_runtime_type=source.GetType().FullName,expected_managed_type=typeof(T).FullName,
                resident_pointer_present=pointer!=IntPtr.Zero,resident_identity_live=identity,database_affinity=databaseMatches,
                handle=!id.IsNull&&id.IsValid?Validation.NormalizeHandle(id.Handle.ToString()):null,
                owner_handle=!owner.IsNull&&owner.IsValid?Validation.NormalizeHandle(owner.Handle.ToString()):null,
                class_observation=pointer!=IntPtr.Zero?classDetails:null,
                scope="Native descriptor equality or SDK-proved inheritance, resident identity and database affinity; no name-based cast or ownership waiver."};
            try{SerializedCapacity357.Check(details,8192,cancel);}
            catch(DomainException error) when(error.Error.code=="RESPONSE_LIMIT_EXCEEDED")
            {
                cancel();var bytes=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(details,Wire.Json);cancel();
                details=new{managed_runtime_type=source.GetType().FullName,expected_managed_type=typeof(T).FullName,
                    resident_pointer_present=pointer!=IntPtr.Zero,resident_identity_live=identity,database_affinity=databaseMatches,
                    compatible_descriptor=compatible,full_diagnostic_bytes=bytes.Length,
                    full_diagnostic_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                    class_names_observation="Complete names/details exceed8KiB; actual bytes/fingerprint retained, values not presented as empty.",
                    scope="Diagnostic budget does not replace the original native-compatible Section binding refusal."};
            }
            throw new DomainException("SECTION_NATIVE_BINDING_UNAVAILABLE","The resident object's native class has no proved compatible Section base interface, or its identity/database is inconsistent.",false,details);
        }
        if(source is T typed&&source.GetType()==typeof(T))return new(source,typed,false);
        var made=Rx.DisposableWrapper.Create(typeof(T),pointer,false);
        // Only a factory result with proved non-owning pointer/identity may be
        // adopted/disposed. Never reset flags or dispose an unsafe returned alias.
        if(made is not T||made.GetType()!=typeof(T)||made.AutoDelete||made.UnmanagedObject!=pointer)
            Reject(made,"The SDK did not return a proved non-owning exact Section wrapper; unsafe factory result was quarantined without disposal.");
        var value=(T)made;
        var binding=new Binding<T>(source,value,true);
        try
        {
            Need(value.ObjectId==id&&value.OwnerId==owner&&value.IsWriteEnabled==source.IsWriteEnabled&&value.Database?.UnmanagedObject==database.UnmanagedObject&&
                Compatible(value,typeof(T),out _,out var borrowedClass)&&borrowedClass==nativeClass,
                "The borrowed Section wrapper changed resident native identity, owner or class.");
            return binding;
        }
        catch{binding.Dispose();throw;}
    }
    internal static Binding<Db.Section> Section(Db.DBObject value,Db.Database db,Action cancel)=>Bind<Db.Section>(value,db,cancel);
    internal static Binding<Db.SectionSettings> Settings(Db.DBObject value,Db.Database db,Action cancel)=>Bind<Db.SectionSettings>(value,db,cancel);
}
