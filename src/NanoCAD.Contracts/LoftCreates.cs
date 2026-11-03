using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record CircularLoftSection(Point3 Center,double Radius);
public sealed record LoftedSolidSpec(string Interpolation,CircularLoftSection[] Sections,string LayerName):EntitySpec("Solid3d",LayerName);

public static partial class CreateValidation
{
    private static EntitySpec ParseLoft(JsonElement item,string path,string layer)
    {
        Shape(item,path,"type","kind","layer","sections","interpolation");
        string interpolation=Text(item,"interpolation",path,16);
        if(interpolation is not ("smooth" or "ruled"))throw Invalid(path+".interpolation","Expected explicit smooth or ruled interpolation.");
        var values=Required(item,"sections",path);
        if(values.ValueKind!=JsonValueKind.Array || values.GetArrayLength()<2)
            throw Invalid(path+".sections","Expected at least two circular sections.","INVALID_GEOMETRY");
        var sections=new List<CircularLoftSection>();
        foreach(var value in values.EnumerateArray())
        {
            string label=path+".sections["+sections.Count+"]";
            Shape(value,label,"center","radius");
            var center=TripleValue(value,"center",label);
            double radius=Number(Required(value,"radius",label),label+".radius",1e-3,1e6);
            if(sections.Count>0)
            {
                var prev=sections[^1].Center;
                double distance=Math.Sqrt(Math.Pow(center.X-prev.X,2)+Math.Pow(center.Y-prev.Y,2)+Math.Pow(center.Z-prev.Z,2));
                if(center.Z-prev.Z<1e-3 || center.Z-sections[0].Center.Z>1e6 || distance>1e6)
                    throw Invalid(label+".center","Section Z must increase by at least 0.001 mm; total height and adjacent center distance are limited to 1000000 mm.","INVALID_GEOMETRY");
            }
            sections.Add(new(center,radius));
        }
        return new LoftedSolidSpec(interpolation,sections.ToArray(),layer);
    }
}
