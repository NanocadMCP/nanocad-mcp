using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;
namespace NanoCADBridge;
internal static class ModelSketchConstraintParameterWriter379
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is ModelSketchConstraintParameterSpec379);
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>NativeSketchConstraintWriter385.Preflight(args,state,tx,cancel);
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)=>NativeSketchConstraintWriter385.Execute(args,state,job,context,log);
}
