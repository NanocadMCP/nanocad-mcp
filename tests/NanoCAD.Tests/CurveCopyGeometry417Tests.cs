using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCADBridge;

internal static class CurveCopyGeometry417Tests
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static bool Exact(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    static JsonElement Shape()
    {
        var controls=Enumerable.Range(0,139).Select(i=>new[]{Math.Cos(i*.2)*12,Math.Sin(i*.2)*12,i*.1}).ToArray();
        return Wire.Element(new{runtime_type="Teigha.DatabaseServices.Spline",closed=false,
            start_parameter=0d,end_parameter=14d,start_point=controls[0],end_point=controls[^1],
            geometry=new{degree=3,rational=false,periodic=false,closed=false,has_fit_data=true,
                control_points=controls,knots=Enumerable.Range(0,143).Select(i=>i*.1).ToArray(),
                weights=Enumerable.Repeat(1d,139).ToArray(),control_point_tolerance=1e-10,knot_tolerance=1e-10,
                start_parameter=0d,end_parameter=14d,start_point=controls[0],end_point=controls[^1],
                native_inspection=new{scope="complete_reported_native_nurbs",fit_data=new{degree=3,points=controls.Take(3).ToArray(),fit_tolerance=.01},
                    fit_data_unavailable_reason=(string?)null,entity_definition=new{degree=3,control_point_count=139,rational=false,closed=false}}}});
    }
    static JsonElement Change(JsonElement value,Action<JsonObject> edit)
    {var root=JsonNode.Parse(value.GetRawText())!.AsObject();edit(root);return Wire.Element(root);}
    static JsonElement FitOnly(JsonElement value)=>Change(value,root=>
    {
        var geometry=root["geometry"]!.AsObject();geometry["has_fit_data"]=false;
        geometry["native_inspection"]!["fit_data"]=null;
        geometry["native_inspection"]!["fit_data_unavailable_reason"]="NATIVE_SPLINE_FIT_UNAVAILABLE: NotApplicable";
    });

    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("block417 detached spline metadata comparison serves all three sweep routes",()=>
        {
            var source=Shape();string original=source.GetRawText();var copy=FitOnly(source);
            var routes=new[]{("direct_loft_sweep","NATIVE_LOFT_SOURCE_COPY_MISMATCH"),
                ("resident_face_path","NATIVE_PATH_CLONE_MISMATCH"),("independent_face_profile_path","NATIVE_PATH_CLONE_MISMATCH")};
            foreach(var (route,code) in routes)
            {
                var warnings=new List<string>();
                CadCurveCopyGeometry417.Require(source,copy,true,Exact,code,"copy differs",route,()=>{},warnings);
                Need(warnings.Count==1&&warnings[0].Contains(route)&&warnings[0].Contains("$.geometry.has_fit_data")&&
                    warnings[0].Contains("NATIVE_SPLINE_COPY_METADATA_NORMALIZED"),"Accepted metadata normalization lost its actual bounded field/route warning.");
            }
            Need(source.GetRawText()==original&&!Exact(source,copy),"Clone-only preparation mutated or normalized the original retained snapshot.");
            var unchanged=CadCurveCopyGeometry417.Compare(source,source,false,Exact,()=>{});
            Need(unchanged.equal&&!unchanged.optional_metadata_normalized&&unchanged.observed_differences.fields.Length==0,"Old equality fastpath changed.");
            Need(!CadCurveCopyGeometry417.Compare(source,copy,false,Exact,()=>{}).equal,"Metadata fallback expanded beyond an exact Spline caller.");
            return Task.CompletedTask;
        });

        await test("block417 complete defining spline changes remain rejected despite optional fit differences",()=>
        {
            var source=Shape();var metadata=FitOnly(source);
            Action<JsonObject>[] changes=[
                r=>r["runtime_type"]="Teigha.DatabaseServices.Helix",r=>r["closed"]=true,
                r=>r["start_parameter"]=-1d,r=>r["end_parameter"]=15d,r=>r["end_point"]![2]=20d,
                r=>r["geometry"]!["degree"]=4,r=>r["geometry"]!["rational"]=true,
                r=>r["geometry"]!["periodic"]=true,r=>r["geometry"]!["closed"]=true,
                r=>r["geometry"]!["control_points"]![69]![0]=20d,
                r=>r["geometry"]!["control_points"]!.AsArray().RemoveAt(69),
                r=>r["geometry"]!["knots"]![70]=8d,r=>r["geometry"]!["weights"]![69]=.5,
                r=>r["geometry"]!["control_point_tolerance"]=2e-10,r=>r["geometry"]!["knot_tolerance"]=2e-10,
                r=>r["geometry"]!["start_parameter"]=-1d,r=>r["geometry"]!["end_parameter"]=15d,
                r=>r["geometry"]!["start_point"]![0]=20d,r=>r["geometry"]!["end_point"]![2]=20d,
                r=>r["geometry"]!.AsObject().Remove("weights"),r=>r["geometry"]!["unknown_future_field"]=1d];
            foreach(var change in changes)
                Need(!CadCurveCopyGeometry417.Compare(source,Change(metadata,change),true,Exact,()=>{}).equal,"A defining/missing/unknown native field was lost from the clone guard.");
            // Existing face routes retain their prior fastpath, but the new
            // metadata-only fallback must not borrow a loose numeric epsilon.
            bool FitUnchanged(JsonElement a,JsonElement b)=>a.GetProperty("geometry").GetProperty("has_fit_data").GetBoolean()==
                b.GetProperty("geometry").GetProperty("has_fit_data").GetBoolean();
            var tinyToleranceChange=Change(metadata,r=>r["geometry"]!["knot_tolerance"]=2e-10);
            Need(!CadCurveCopyGeometry417.Compare(source,tinyToleranceChange,true,FitUnchanged,()=>{}).equal,"New fallback accepted a changed tolerance through caller numeric policy.");
            return Task.CompletedTask;
        });

        await test("block417 mismatch diagnostics are bounded nested actual values and preserve cancellation",()=>
        {
            var source=Shape();var changed=Change(FitOnly(source),r=>
            {foreach(var point in r["geometry"]!["control_points"]!.AsArray())point![0]=42d;});
            try
            {
                CadCurveCopyGeometry417.Require(source,changed,true,Exact,"NATIVE_PATH_CLONE_MISMATCH","copy differs","resident_face_path",()=>{});
                throw new Exception("Changed complete NURBS was accepted.");
            }
            catch(DomainException error)
            {
                Need(error.Error.code=="NATIVE_PATH_CLONE_MISMATCH","The owning route's failure code changed.");
                var details=Wire.Element(error.Error.details??throw new Exception("Clone rejection omitted its diagnostics."));var compared=details.GetProperty("copy_comparison");
                var differences=compared.GetProperty("defining_differences");
                Need(differences.GetProperty("truncated").GetBoolean()&&differences.GetProperty("fields").GetArrayLength()==8&&
                    differences.GetProperty("fields")[0].GetProperty("path").GetString()!.Contains("control_points[0][0]")&&
                    differences.GetProperty("fields")[0].GetProperty("copy").GetString()=="42"&&details.GetRawText().Length<8192,
                    "Diagnostics omitted real nested values or serialized huge arrays.");
            }
            int visits=0;
            try{CadCurveCopyGeometry417.Compare(source,changed,true,Exact,()=>{if(++visits==3)throw new OperationCanceledException();});throw new Exception("Cancellation was swallowed.");}
            catch(OperationCanceledException){Need(visits==3,"Comparison continued after cancellation.");}
            return Task.CompletedTask;
        });
    }
}
