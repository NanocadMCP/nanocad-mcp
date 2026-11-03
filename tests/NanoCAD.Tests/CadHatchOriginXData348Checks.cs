using System.Globalization;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Isolated managed checks. Compile together with the exact proposed helper and
// Wire JSON serializer; no native SDK type or assembly is required.
internal static class CadHatchOriginXData348Checks
{
    internal static void Run()
    {
        object Scalar(double value)=>new{native_type="double",bits=BitConverter.DoubleToInt64Bits(value).ToString("X16",CultureInfo.InvariantCulture)};
        object Point(double x,double y)=>new{code=1010,value=new{native_type="Point3d",coordinates=new[]{Scalar(x),Scalar(y),Scalar(0d)}}};
        object Acad()=>new{code=1001,value="ACAD"};
        object Foreign()=>new{code=1001,value="ENGINEERING_DATA"};
        object Data(string value)=>new{code=1000,value};
        JsonElement Metadata(object?[]? data,string layer="LAYER1")=>Wire.Element(new{layer_handle=layer,xdata=data,owned_metadata=new object[0]});
        bool Same(JsonElement before,JsonElement after,bool setter=true,double[]? oldOrigin=null,double[]? currentOrigin=null)=>
            CadHatchOriginXData348.MetadataPreserved(before,after,oldOrigin??new[]{0d,0d},currentOrigin??new[]{2d,3d},setter,()=>{});
        void Require(bool condition,string name){if(!condition)throw new InvalidOperationException("Hatch Origin check failed: "+name);}
        var empty=Metadata(null);var origin=Metadata(new[]{Acad(),Point(2,3)});
        Require(Same(empty,origin),"native setter may add only classified Origin");
        Require(Same(Metadata(Array.Empty<object?>()),origin),"empty buffer may gain classified Origin");
        Require(!Same(empty,origin,false),"without setter new data stays forbidden");
        Require(Same(empty,empty,false),"exact retained state remains valid");
        Require(Same(Metadata(new[]{Acad(),Point(0,0)}),origin),"recognized old Origin may be replaced");
        Require(!Same(Metadata(new[]{Acad(),Point(8,9)}),origin),"old XData must match captured old Origin");
        Require(!Same(empty,origin,true,currentOrigin:new[]{2d,4d}),"new XData must match verified current Origin");
        var foreign=Metadata(new[]{Foreign(),Data("required machining note")});
        var foreignWithOrigin=Metadata(new[]{Foreign(),Data("required machining note"),Acad(),Point(2,3)});
        Require(Same(foreign,foreignWithOrigin),"all foreign app records retained exactly");
        Require(!Same(foreign,Metadata(new[]{Foreign(),Data("changed note"),Acad(),Point(2,3)})),"foreign data change is rejected");
        Require(!Same(foreign,origin),"foreign data removal is rejected");
        Require(!Same(empty,Metadata(new[]{Acad(),Point(2,3),Data("unclassified ACAD field")})),"extended ACAD group rejected");
        Require(!Same(empty,Metadata(new[]{Acad(),Point(2,3),Acad(),Point(2,3)})),"duplicate ACAD group rejected");
        Require(!Same(empty,Metadata(new[]{Acad(),Point(2,3)},"OTHER_LAYER")),"other metadata remains exact");
        Require(!Same(Metadata(new[]{Acad(),Point(0,0)}),empty),"unsupported group deletion rejected");
        bool cancelled=false;
        try{CadHatchOriginXData348.MetadataPreserved(empty,origin,new[]{0d,0d},new[]{2d,3d},true,()=>throw new OperationCanceledException());}
        catch(OperationCanceledException){cancelled=true;}
        Require(cancelled,"cancellation propagates");
    }
}
