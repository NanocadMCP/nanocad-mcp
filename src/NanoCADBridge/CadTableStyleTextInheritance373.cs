using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Resolve only an observed native named identity or the actual row role of an
// empty assigned name. Display/localized names never manufacture a role alias.
internal static class CadTableStyleTextInheritance373
{
    internal sealed record Defaults(string? Font,double Height,JsonElement? Named,string Provenance);
    internal static string Handle(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    static string Label(string value)=>Encoding.UTF8.GetByteCount(value)<=512?value:value[..Math.Min(value.Length,128)]+" [diagnostic truncated]";
    static object Details(string handle,int row,int column,string name,string role,string phase,object evidence)=>new
    {
        table_handle=handle,row,column,actual_row_role=role,assigned_cell_style=Label(name),
        assigned_cell_style_sha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))),phase,evidence
    };
    static DomainException Failure(string code,string reason,string handle,int row,int column,string name,string role,string phase,object evidence)
    {
        var details=Details(handle,row,column,name,role,phase,evidence);
        if(JsonSerializer.SerializeToUtf8Bytes(details,Wire.Json).Length>4096)
            return new(code,reason,false,new{table_handle=handle,row,column,actual_row_role=role,phase,diagnostic_omitted=true,
                assigned_cell_style_sha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))});
        return new(code,reason,false,details);
    }
    internal static string[] RowRoles(Table table,Action cancel)
    {
        var roles=new string[table.Rows.Count];
        for(int row=0;row<roles.Length;row++)
        {
            cancel();roles[row]=table.RowType(row).ToString();
            if(roles[row] is not ("TitleRow" or "HeaderRow" or "DataRow"))
                throw Failure("UNSUPPORTED_TABLE_STYLE_CONSUMER","Actual native Table row-role identity is unavailable.",Handle(table.ObjectId),row,-1,"",roles[row],"capture_row_roles",new{native_row_role_unavailable=true});
        }
        return roles;
    }
    internal static Defaults Resolve(CadTableStyleWriter337.Stored style,string role,string name,string handle,int row,int column,string phase,Action cancel)
    {
        cancel();
        if(name.Length==0)
        {
            var rows=style.rows.Where(value=>value.row_type==role).ToArray();
            if(rows.Length!=1)throw Failure("UNSUPPORTED_TABLE_STYLE_CONSUMER","Empty assigned native cell-style name needs its unique observed RowType defaults.",handle,row,column,name,role,phase,new{matching_row_defaults=rows.Length});
            return new(rows[0].text_style_handle,rows[0].text_height,null,"empty_name_actual_native_RowType");
        }
        // Native CellStyles/TextStyle(name)/TextHeight(name) were captured
        // together. An exact named Data assignment on a title remains Data.
        var matching=new List<JsonElement>();int count=0;
        foreach(var value in style.named_cell_styles.EnumerateArray())
        {cancel();count++;if(value.GetProperty("name").GetString()==name)matching.Add(value);}
        if(matching.Count!=1)throw Failure("UNSUPPORTED_TABLE_STYLE_CONSUMER","Assigned nonempty native cell-style name has no unique observed style identity; its scoped native alias adapter is required.",handle,row,column,name,role,phase,new{observed_named_styles=count,exact_name_matches=matching.Count,physical_row_fallback=false});
        var named=matching[0];return new(named.GetProperty("text_style_handle").GetString(),named.GetProperty("text_height").GetDouble(),named,"exact_native_named_cell_style");
    }
    internal static string EffectiveFont(Table table,int row,int column,int? content,Database db,Transaction tr,Action cancel)
    {
        cancel();var id=content is {} index?table.GetTextStyleId(row,column,index):table.TextStyle(row,column);
        if(id.IsNull||!id.IsValid||id.IsErased||id.IsEffectivelyErased)
            throw Failure("STYLE_UNAVAILABLE","Actual native effective Table text-style identity is unavailable.",Handle(table.ObjectId),row,column,table.GetCellStyle(row,column),table.RowType(row).ToString(),"native_effective_font",new{content_index=content});
        var item=tr.GetObject(id,OpenMode.ForRead);
        if(item is not TextStyleTableRecord||item.ObjectId!=id||db.UnmanagedObject==IntPtr.Zero||
            item.Database?.UnmanagedObject!=db.UnmanagedObject||item.OwnerId!=db.TextStyleTableId)
            throw Failure("STYLE_UNAVAILABLE","Actual effective Table font is not owned by the current text-style table.",Handle(table.ObjectId),row,column,table.GetCellStyle(row,column),table.RowType(row).ToString(),"native_effective_font",new{content_index=content,text_style_handle=Handle(id)});
        return Handle(id);
    }
    internal static void VerifyText(string handle,int row,int column,string name,string role,Defaults before,Defaults after,
        string priorFont,string actualFont,double priorHeight,double actualHeight,bool fontSelected,bool heightSelected,bool explicitFont,bool explicitHeight)
    {
        bool font=explicitFont||!fontSelected||before.Font==after.Font||priorFont!=before.Font?
            actualFont==priorFont:actualFont==priorFont||actualFont==after.Font;
        bool height=explicitHeight||!heightSelected||before.Height==after.Height||priorHeight!=before.Height?
            actualHeight==priorHeight:actualHeight==priorHeight||actualHeight==after.Height;
        if(!font||!height)throw Failure("NATIVE_TABLE_STYLE_MISMATCH","Native Table text inheritance changed an explicit/unrequested value or an unproved third effective font/height.",handle,row,column,name,role,"after_native_layout",new
        {
            font_allowed=font,height_allowed=height,explicit_font=explicitFont,explicit_height=explicitHeight,
            old_default_font=before.Font,new_default_font=after.Font,prior_font=priorFont,actual_font=actualFont,
            old_default_height=before.Height,new_default_height=after.Height,prior_height=priorHeight,actual_height=actualHeight,
            before_provenance=before.Provenance,after_provenance=after.Provenance
        });
    }
}
