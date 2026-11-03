using System.Runtime.CompilerServices;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelSketchConstraintParameterReader379
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static ModelSketchConstraintParameterRead379[] Read(Arguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var output=new List<ModelSketchConstraintParameterRead379>();var bytes=new ModelInventoryBudget(cancel);
        foreach(string id in args.SketchConstraintParameterIds??[])
        {
            cancel();ModelSketchConstraintParameterRead379 item;
            item=NativeSketchConstraintWriter385.ReadControl(args,state,tx,id,cancel);
            bytes.Add(item);output.Add(item);
        }
        return output.ToArray();
    }
}
