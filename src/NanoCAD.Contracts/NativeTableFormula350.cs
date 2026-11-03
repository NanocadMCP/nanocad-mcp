using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NanoCAD.Contracts;

// Syntax and dependency accounting only. Native Table.SetFormula evaluates.
public static class NativeTableFormula350
{
    const int Budget=8*1024*1024;
    public static readonly string[] Functions=["SUM","AVERAGE","COUNT"];
    static DomainException Invalid(string message)=>new("UNSUPPORTED_TABLE_FORMULA",message);
    static bool AsciiLetter(char ch)=>ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    static bool AsciiDigit(char ch)=>ch is >= '0' and <= '9';
    // BEGIN inherited343 arithmetic parser with native aggregates350
    public static TableFormulaSyntax343 Inspect(string formula,Action? cancel=null)
    {
        void Check()=>cancel?.Invoke();
        Check();int bytes;
        try{bytes=new UTF8Encoding(false,true).GetByteCount(formula);}catch(EncoderFallbackException){throw Invalid("Native formula contains invalid Unicode text.");}
        if(bytes>Budget)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Native formula exceeds the actual8MiB snapshot budget.");
        var canonical=new StringBuilder();var operators=new Stack<char>();var refs=new HashSet<TableFormulaReference343>();var strictRefs=new HashSet<TableFormulaReference343>();var aggregateRefs=new HashSet<TableFormulaReference343>();var ranges=new List<TableFormulaRangeReference350>();int at=0,depth=0;bool operand=true,seen=false;
        void Emit(string token){canonical.Append(token).Append(';');if(canonical.Length>Budget)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Native formula syntax exceeds the actual8MiB snapshot budget.");}
        static int Priority(char op)=>op is 'P' or 'N'?3:op is '*' or '/'?2:1;
        void Operator(char op)
        {
            while(operators.TryPeek(out char previous)&&previous!='('&&Priority(previous)>=Priority(op)){Check();Emit(operators.Pop().ToString());}
            operators.Push(op);
        }
        void Space(){while(at<formula.Length&&formula[at]==' '){Check();at++;}}
        Space();if(at<formula.Length&&formula[at]=='='){at++;Space();}
        while(at<formula.Length)
        {
            Check();char ch=formula[at];
            if(ch==' '){Space();continue;}
            if(ch=='(')
            {if(!operand)throw Invalid("A native formula requires an operator before a parenthesis.");depth++;operators.Push(ch);at++;continue;}
            if(ch==')')
            {if(operand||depth==0)throw Invalid("Native formula parentheses are incomplete.");while(operators.Peek()!='('){Check();Emit(operators.Pop().ToString());}operators.Pop();depth--;at++;operand=false;continue;}
            if(ch is '+' or '-')
            {if(operand)operators.Push(ch=='+'?'P':'N');else Operator(ch);at++;operand=true;continue;}
            if(ch is '*' or '/')
            {if(operand)throw Invalid("Native formula requires a numeric operand before multiplication/division.");Operator(ch);at++;operand=true;continue;}
            if(!operand)throw Invalid("Native formula requires an operator between operands.");
            if(AsciiLetter(ch))
            {
                int word=at;while(word<formula.Length&&AsciiLetter(formula[word])){Check();word++;}
                int after=word;NativeTableFormula350.Space(formula,ref after,Check);
                if(after<formula.Length&&formula[after]=='(')
                {Emit(Aggregate(formula,ref at,refs,aggregateRefs,ranges,Check));seen=true;operand=false;continue;}
            }
            if(AsciiLetter(ch)||ch=='$')
            {
                bool absoluteColumn=ch=='$';if(absoluteColumn)at++;
                long column=0;int letters=0;
                while(at<formula.Length&&AsciiLetter(formula[at]))
                {Check();column=column*26+char.ToUpperInvariant(formula[at])- 'A'+1;if(column>int.MaxValue)throw Invalid("Native column reference exceeds the SDK integer representation.");letters++;at++;}
                if(letters==0)throw Invalid("Use an actual A1 cell reference.");
                bool absoluteRow=at<formula.Length&&formula[at]=='$';if(absoluteRow)at++;
                int start=at;while(at<formula.Length&&AsciiDigit(formula[at])){Check();at++;}
                if(start==at||!int.TryParse(formula.AsSpan(start,at-start),NumberStyles.None,CultureInfo.InvariantCulture,out int row)||row<1)
                    throw Invalid("Use a same-table A1 reference or a published native aggregate; external/named references need their adapter.");
                var reference=new TableFormulaReference343(row-1,checked((int)column-1));refs.Add(reference);strictRefs.Add(reference);
                Emit("R"+(absoluteRow?"$":"")+reference.Row.ToString(CultureInfo.InvariantCulture)+","+(absoluteColumn?"$":"")+reference.Column.ToString(CultureInfo.InvariantCulture));
            }
            else if(AsciiDigit(ch)||ch=='.')
            {
                int start=at;bool dot=false;
                while(at<formula.Length&&(AsciiDigit(formula[at])||formula[at]=='.'&&!dot))
                {Check();if(formula[at]=='.')dot=true;at++;}
                if(at<formula.Length&&formula[at] is 'e' or 'E')
                {at++;if(at<formula.Length&&formula[at] is '+' or '-')at++;int exponent=at;while(at<formula.Length&&AsciiDigit(formula[at])){Check();at++;}if(at==exponent)throw Invalid("Native numeric exponent is incomplete.");}
                if(!double.TryParse(formula.AsSpan(start,at-start),NumberStyles.Float,CultureInfo.InvariantCulture,out double number)||!double.IsFinite(number))
                    throw Invalid("Use finite numeric literals in the native formula.");
                Emit("V"+number.ToString("R",CultureInfo.InvariantCulture));
            }
            else throw Invalid("Use same-table A1 arithmetic with + - * / parentheses and native SUM/AVERAGE/COUNT of cell references/ranges; fields and external references need their adapter.");
            seen=true;operand=false;
        }
        Check();if(!seen||operand||depth!=0)throw Invalid("Native formula arithmetic is incomplete.");
        while(operators.Count>0){Check();Emit(operators.Pop().ToString());}
        return new(canonical.ToString(),refs.OrderBy(r=>r.Row).ThenBy(r=>r.Column).ToArray(),aggregateRefs.Except(strictRefs).OrderBy(r=>r.Row).ThenBy(r=>r.Column).ToArray(),ranges.ToArray());
    }
    // END inherited343 arithmetic parser with native aggregates350


    readonly record struct Reference(TableFormulaReference343 Cell,bool RowAbsolute,bool ColumnAbsolute);
    static string Key(Reference value)=>"R"+(value.RowAbsolute?"$":"")+value.Cell.Row.ToString(CultureInfo.InvariantCulture)+","+
        (value.ColumnAbsolute?"$":"")+value.Cell.Column.ToString(CultureInfo.InvariantCulture);
    static void Space(string text,ref int at,Action cancel){while(at<text.Length&&text[at]==' '){cancel();at++;}}
    static Reference ReadReference(string text,ref int at,Action cancel)
    {
        bool columnAbsolute=at<text.Length&&text[at]=='$';if(columnAbsolute)at++;
        long column=0;int letters=0;
        while(at<text.Length&&AsciiLetter(text[at]))
        {cancel();column=column*26+char.ToUpperInvariant(text[at])-'A'+1;if(column>int.MaxValue)throw Invalid("Column exceeds native integer indices.");letters++;at++;}
        if(letters==0)throw Invalid("An aggregate argument needs an A1 reference or rectangular A1 range.");
        bool rowAbsolute=at<text.Length&&text[at]=='$';if(rowAbsolute)at++;
        int first=at;while(at<text.Length&&AsciiDigit(text[at])){cancel();at++;}
        if(first==at||!int.TryParse(text.AsSpan(first,at-first),NumberStyles.None,CultureInfo.InvariantCulture,out int row)||row<1)
            throw Invalid("An aggregate argument needs a positive native A1 row.");
        return new(new(row-1,(int)column-1),rowAbsolute,columnAbsolute);
    }
    static string Aggregate(string text,ref int at,HashSet<TableFormulaReference343> references,
        HashSet<TableFormulaReference343> aggregateReferences,List<TableFormulaRangeReference350> ranges,Action cancel)
    {
        int first=at;while(at<text.Length&&AsciiLetter(text[at])){cancel();at++;}
        string name=text[first..at].ToUpperInvariant();if(!Functions.Contains(name,StringComparer.Ordinal))throw Invalid("Use documented native SUM, AVERAGE or COUNT.");
        Space(text,ref at,cancel);if(at==text.Length||text[at++]!='(')throw Invalid("Native aggregate needs argument parentheses.");
        var canonical=new StringBuilder("F:"+name+"[");int count=0;
        void Add(TableFormulaReference343 cell)
        {
            cancel();references.Add(cell);aggregateReferences.Add(cell);
        }
        while(true)
        {
            Space(text,ref at,cancel);var start=ReadReference(text,ref at,cancel);Space(text,ref at,cancel);
            canonical.Append(Key(start));
            if(at<text.Length&&text[at]==':')
            {
                at++;Space(text,ref at,cancel);var end=ReadReference(text,ref at,cancel);Space(text,ref at,cancel);
                if(end.Cell.Row<start.Cell.Row||end.Cell.Column<start.Cell.Column)throw Invalid("Use a rectangular range from top-left to bottom-right.");
                ranges.Add(new(start.Cell,end.Cell));
                canonical.Append(':').Append(Key(end));
            }
            else Add(start.Cell);
            if(canonical.Length>Budget)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Native aggregate syntax exceeds actual8MiB bytes.");
            count++;if(at==text.Length)throw Invalid("Native aggregate arguments are incomplete.");
            char separator=text[at++];if(separator==')')break;if(separator!=',')throw Invalid("Separate native aggregate references/ranges with a comma.");
            canonical.Append('|');
        }
        if(count==0)throw Invalid("A native aggregate needs at least one reference.");return canonical.Append(']').ToString();
    }
    public static TableFormulaSyntax343 Expand(TableFormulaSyntax343 syntax,int rows,int columns,Action cancel)
    {
        var references=syntax.References.ToHashSet();var aggregate=(syntax.AggregateOnlyReferences??[]).ToHashSet();
        var strict=references.Except(aggregate).ToHashSet();long bytes=0;
        void Admit(TableFormulaReference343 reference)
        {
            cancel();if(reference.Row<0||reference.Column<0||reference.Row>=rows||reference.Column>=columns)
                throw new DomainException("CELL_OUT_OF_RANGE","Native formula reference is outside this exact Table.");
            bytes+=JsonSerializer.SerializeToUtf8Bytes(reference,Wire.Json).Length+1;
            if(bytes>Budget)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Complete native formula dependencies exceed actual8MiB bytes.");
        }
        foreach(var reference in references)Admit(reference);
        foreach(var range in syntax.Ranges??[])
        {
            cancel();if(range.First.Row<0||range.First.Column<0||range.Last.Row>=rows||range.Last.Column>=columns)
                throw new DomainException("CELL_OUT_OF_RANGE","Native aggregate rectangle is outside this exact Table.");
            for(long row=range.First.Row;row<=range.Last.Row;row++)for(long column=range.First.Column;column<=range.Last.Column;column++)
            {cancel();var reference=new TableFormulaReference343((int)row,(int)column);if(references.Add(reference))Admit(reference);if(!strict.Contains(reference))aggregate.Add(reference);}
        }
        return syntax with{References=references.OrderBy(item=>item.Row).ThenBy(item=>item.Column).ToArray(),
            AggregateOnlyReferences=aggregate.OrderBy(item=>item.Row).ThenBy(item=>item.Column).ToArray()};
    }
    // Structural changes move every retained cell, including dollar anchors.
    // A deleted reference is a design decision, never a fabricated #REF/value.
    public static string RemapStructural(string formula,bool rows,bool insert,int index,int count,Action? cancel=null)
    {
        void Check()=>cancel?.Invoke();_ =Inspect(formula,Check);
        if(index<0||count<=0)throw new DomainException("INVALID_ARGUMENT","Use a nonnegative structural index and positive count.");
        var result=new StringBuilder();int at=0;long end=(long)index+count;
        DomainException Removed()=>new("TABLE_FORMULA_REFERENCE_REMOVED","This retained native formula addresses a removed cell or entire range. Supply formula_replacements for its before-operation anchor using final table coordinates.");
        int Map(int value)
        {
            Check();if(!insert&&value>=index&&value<end)throw Removed();
            long mapped=insert&&value>=index?(long)value+count:!insert&&value>=end?(long)value-count:value;
            if(mapped<0||mapped>=int.MaxValue)throw Invalid("Structural formula mapping exceeds native A1 indices.");return(int)mapped;
        }
        Reference Single(Reference value)=>value with{Cell=rows?value.Cell with{Row=Map(value.Cell.Row)}:value.Cell with{Column=Map(value.Cell.Column)}};
        (Reference First,Reference Last) Range(Reference first,Reference last)
        {
            if(insert)return(Single(first),Single(last));
            int a=rows?first.Cell.Row:first.Cell.Column,b=rows?last.Cell.Row:last.Cell.Column;
            long survivingFirst=a>=index&&a<end?end:a,survivingLast=b>=index&&b<end?(long)index-1:b;
            if(survivingFirst>survivingLast||survivingFirst>b||survivingLast<a)throw Removed();
            if(survivingFirst>=int.MaxValue)throw Removed();
            var f=rows?first with{Cell=first.Cell with{Row=Map((int)survivingFirst)}}:first with{Cell=first.Cell with{Column=Map((int)survivingFirst)}};
            var l=rows?last with{Cell=last.Cell with{Row=Map((int)survivingLast)}}:last with{Cell=last.Cell with{Column=Map((int)survivingLast)}};return(f,l);
        }
        void Emit(Reference value)
        {
            string letters=NativeTableFormula343.CellName(new(0,value.Cell.Column))[..^1];
            if(value.ColumnAbsolute)result.Append('$');result.Append(letters);if(value.RowAbsolute)result.Append('$');result.Append((long)value.Cell.Row+1);
        }
        while(at<formula.Length)
        {
            Check();int first=at;char ch=formula[at];
            if(AsciiDigit(ch)||ch=='.')
            {
                while(at<formula.Length&&(AsciiDigit(formula[at])||formula[at]=='.')){Check();at++;}
                if(at<formula.Length&&formula[at] is 'e' or 'E')
                {at++;if(at<formula.Length&&formula[at] is '+' or '-')at++;while(at<formula.Length&&AsciiDigit(formula[at])){Check();at++;}}
                result.Append(formula.AsSpan(first,at-first));continue;
            }
            if(AsciiLetter(ch)||ch=='$')
            {
                int word=at;while(word<formula.Length&&AsciiLetter(formula[word])){Check();word++;}
                int next=word;Space(formula,ref next,Check);
                if(next<formula.Length&&formula[next]=='('){result.Append(formula.AsSpan(at,word-at));at=word;continue;}
                var reference=ReadReference(formula,ref at,Check);int colon=at;Space(formula,ref colon,Check);
                if(colon<formula.Length&&formula[colon]==':')
                {
                    at=colon+1;Space(formula,ref at,Check);var last=ReadReference(formula,ref at,Check);var range=Range(reference,last);Emit(range.First);result.Append(':');Emit(range.Last);
                }
                else Emit(Single(reference));continue;
            }
            result.Append(ch);at++;
        }
        string remapped=result.ToString();_ =Inspect(remapped,Check);return remapped;
    }

    public static string Shift(string formula,int rowDelta,int columnDelta,Action? cancel=null)
    {
        void Check()=>cancel?.Invoke();_ =Inspect(formula,Check);var result=new StringBuilder();int at=0;
        while(at<formula.Length)
        {
            Check();int first=at;char ch=formula[at];
            if(AsciiDigit(ch)||ch=='.')
            {
                while(at<formula.Length&&(AsciiDigit(formula[at])||formula[at]=='.')){Check();at++;}
                if(at<formula.Length&&formula[at] is 'e' or 'E')
                {at++;if(at<formula.Length&&formula[at] is '+' or '-')at++;while(at<formula.Length&&AsciiDigit(formula[at])){Check();at++;}}
                result.Append(formula.AsSpan(first,at-first));continue;
            }
            if(AsciiLetter(ch)||ch=='$')
            {
                int word=at;while(word<formula.Length&&AsciiLetter(formula[word])){Check();word++;}
                int after=word;Space(formula,ref after,Check);
                if(after<formula.Length&&formula[after]=='('){result.Append(formula.AsSpan(at,word-at));at=word;continue;}
                var reference=ReadReference(formula,ref at,Check);
                long row=(long)reference.Cell.Row+(reference.RowAbsolute?0:rowDelta),column=(long)reference.Cell.Column+(reference.ColumnAbsolute?0:columnDelta);
                if(row<0||column<0||row>=int.MaxValue||column>=int.MaxValue)throw Invalid("Relative formula fill moves an A1 reference outside native indices.");
                string letters=NativeTableFormula343.CellName(new(0,(int)column))[..^1];
                if(reference.ColumnAbsolute)result.Append('$');result.Append(letters);
                if(reference.RowAbsolute)result.Append('$');result.Append(row+1);continue;
            }
            result.Append(ch);at++;
        }
        _=Inspect(result.ToString(),Check);return result.ToString();
    }
}

public sealed record TableFormulaRangeReference350(TableFormulaReference343 First,TableFormulaReference343 Last);
public sealed record TableFormulaRangeSpec350(string[] Targets,string TableToken,int FirstRow,int LastRow,int FirstColumn,int LastColumn,
    string Formula,bool RelativeReferences,string UnitType,string? DataFormat):EditSpec("fill_table_formula_range",Targets);

public sealed record TableFormulaWorkflowRead350(string[] operations,string[] aggregate_functions,string range_scope,
    string relative_fill,string evaluated_values);

public static class TableFormulaRangeValidation350
{
    public const string Operation="fill_table_formula_range";
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static JsonElement Required(JsonElement value,string key,string path)=>value.TryGetProperty(key,out var item)?item:throw Invalid(path+"."+key,"Required field.");
    static void Shape(JsonElement value,string path,params string[] keys)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Use an object.");var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in value.EnumerateObject())if(!keys.Contains(item.Name)||!seen.Add(item.Name))throw Invalid(path+"."+item.Name,"Unknown or duplicate field.");
    }
    static string Text(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.String)throw Invalid(path,"Use a string.");string text;
        try{text=value.GetString()!;_=new UTF8Encoding(false,true).GetByteCount(text);}
        catch(Exception error) when(error is InvalidOperationException or EncoderFallbackException){throw Invalid(path,"Use valid Unicode scalar text.");}
        if(text.Any(char.IsControl))throw Invalid(path,"Control characters are not native formula/format text.");return text;
    }
    static int Index(JsonElement value,string path)=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int index)&&index>=0?index:throw Invalid(path,"Use a nonnegative native cell index.");
    public static TableFormulaRangeSpec350 Parse(JsonElement input,string path,string[] handles)
    {
        Shape(input,path,"operation","handles","table_token","range","formula","relative_references","unit_type","data_format");
        if(handles.Length!=1||Text(Required(input,"operation",path),path)!=Operation)throw Invalid(path,"Select one existing Table formula range.");
        string token=Text(Required(input,"table_token",path),path+".table_token");if(token.Length!=64||!token.All(ch=>"0123456789ABCDEF".Contains(ch)))throw Invalid(path+".table_token","Use the fresh complete uppercase native table_token.");
        var range=Required(input,"range",path);Shape(range,path+".range","first_row","last_row","first_column","last_column");
        int r0=Index(Required(range,"first_row",path),path),r1=Index(Required(range,"last_row",path),path),c0=Index(Required(range,"first_column",path),path),c1=Index(Required(range,"last_column",path),path);
        if(r1<r0||c1<c0)throw Invalid(path+".range","Use an inclusive rectangle from top-left to bottom-right.");
        string formula=Text(Required(input,"formula",path),path+".formula");NativeTableFormula350.Inspect(formula);
        var relative=Required(input,"relative_references",path);if(relative.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(path+".relative_references","Choose explicit relative or repeated references.");
        string unit=Text(Required(input,"unit_type",path),path+".unit_type");if(!TableValueEditingValidation336.UnitTypes.Contains(unit,StringComparer.Ordinal))throw Invalid(path+".unit_type","Use a published native result unit, without implicit conversion.");
        string? format=input.TryGetProperty("data_format",out var data)?Text(data,path+".data_format"):null;
        return new(handles,token,r0,r1,c0,c1,formula,relative.GetBoolean(),unit,format);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {if(edits.Any(edit=>edit is TableFormulaRangeSpec350)&&edits.Count!=1)throw Invalid("operations","Native formula range filling must be the sole operation.");}
    public static TableFormulaWorkflowRead350 Describe()=>new(["set_table_cell_formula",Operation],NativeTableFormula350.Functions,
        "same_table_A1_numeric_arithmetic_and_aggregate_rectangles_or_individual_cells",
        "formula_is_written_for_range_top_left_relative_fill_preserves_dollar_row_and_column_anchors",
        "native_kernel_only_observed_cache_can_be_unavailable_no_external_value_substitution");
}
