using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;
namespace NanoCADBridge;
internal static class ModelParameterWriter
{
    public static void Preflight(ModelParameterArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>ModelParameterGraphWriter410.Preflight(args,state,tx,cancel,numeric:true);
    public static Result Execute(ModelParameterArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)=>ModelParameterGraphWriter410.Execute(args,state,job,context,log,numeric:true);
}