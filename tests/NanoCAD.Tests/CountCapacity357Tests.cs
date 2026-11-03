using System.Text.Json;
using NanoCAD.Contracts;

internal static class CountCapacity357Tests
{
    private static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    private static void Reject(string code,Action operation)
    {try{operation();}catch(DomainException e)when(e.Error.code==code){return;}throw new Exception("Expected "+code);}
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("capacity357 charges complete UTF8 escaped items and array separators",()=>
        {
            object[] entries={new{text="Dimension \"A\"",points=new[]{1.5,2.25}},new{text="\u03a9",points=Array.Empty<double>()},new{text="last",points=new[]{3d}}};
            int bytes=JsonSerializer.SerializeToUtf8Bytes(entries,Wire.Json).Length;
            var exact=new SerializedCapacity357(bytes);foreach(var item in entries)exact.Add(item);
            var smaller=new SerializedCapacity357(bytes-1);foreach(var item in entries.Take(entries.Length-1))smaller.Add(item);
            Reject("RESPONSE_LIMIT_EXCEEDED",()=>smaller.Add(entries[^1]));
            SerializedCapacity357.Check(entries,bytes);Reject("RESPONSE_LIMIT_EXCEEDED",()=>SerializedCapacity357.Check(entries,bytes-1));
            return Task.CompletedTask;
        });
        await test("capacity357 cancellation propagates before and after actual serialization",()=>
        {
            int calls=0;void Cancel(){if(++calls==2)throw new OperationCanceledException();}
            bool cancelled=false;try{new SerializedCapacity357(cancel:Cancel).Add(new{values=Enumerable.Range(0,100).ToArray()});}catch(OperationCanceledException){cancelled=true;}
            Need(cancelled&&calls==2,"Serialization cancellation was swallowed or omitted.");
            calls=0;cancelled=false;try{SerializedCapacity357.Check(new{value="checked"},cancel:Cancel);}catch(OperationCanceledException){cancelled=true;}
            Need(cancelled&&calls==2,"Whole-observation cancellation was swallowed or omitted.");return Task.CompletedTask;
        });
        await test("capacity357 native indexed product keeps minimum and Int32 overflow proof",()=>
        {
            Need(SerializedCapacity357.Product(33,3,2)==99,"Former engineering count ceiling retained.");
            Need(SerializedCapacity357.Product(int.MaxValue,1)==int.MaxValue,"Valid native index representation refused.");
            Reject("INVALID_ARGUMENT",()=>SerializedCapacity357.Product(int.MaxValue,2));
            Reject("INVALID_ARGUMENT",()=>SerializedCapacity357.Product(0,2));
            Reject("INVALID_ARGUMENT",()=>SerializedCapacity357.Product(1,1,2));return Task.CompletedTask;
        });
    }
}
