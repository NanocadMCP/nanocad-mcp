namespace NanoCAD.Contracts;

// Retain a single uniform native formatting envelope. This is deliberately a
// content editor, not a formatter: fields, stacked fractions and mixed runs
// must keep their owning representation instead of being silently flattened.
public static class UniformMTextContent407
{
    public sealed record Envelope(string Prefix,string Suffix,string? Typeface,bool Bold,bool Italic);
    public static Envelope Capture(string contents)
    {
        if(MTextContent.PlainText(contents)==null)
            throw new DomainException("UNSUPPORTED_MTEXT_CONTENT","Stored MTEXT contains fields, fractions or undecodable controls; use its owning content adapter.");
        int first=-1,last=0;var formatting=new List<int>();
        for(int i=0;i<contents.Length;)
        {
            int start=i;char c=contents[i++];bool visible=true;
            if(c is '{' or '}'){visible=false;formatting.Add(start);}
            else if(c=='\r')visible=false;
            else if(c=='^')
            {
                char code=contents[i++];visible=code!='M';
            }
            else if(c=='\\')
            {
                char code=contents[i++];
                if(code=='U')i+=5;
                else if(code is 'C' or 'c' or 'A' or 'H' or 'W' or 'T' or 'Q' or 'F' or 'f' or 'p')
                {
                    int end=contents.IndexOf(';',i);i=end+1;visible=false;formatting.Add(start);
                }
                else if(code is 'L' or 'l' or 'O' or 'o' or 'K' or 'k'){visible=false;formatting.Add(start);}
            }
            if(visible){if(first<0)first=start;last=i;}
        }
        if(first<0)
        {
            if(contents.Length!=0)throw new DomainException("UNSUPPORTED_MTEXT_CONTENT","An empty formatted MTEXT has no unambiguous retained content run.");
            return new("","",null,false,false);
        }
        if(formatting.Any(index=>index>first&&index<last))
            throw new DomainException("UNSUPPORTED_MTEXT_CONTENT","MTEXT contains multiple formatting runs; whole-content replacement would lose their meaning. Use run-level editing instead.");
        // A trailing font code affects no old glyphs and is not the old run's
        // font. Only the prefix is inspected for replacement glyph coverage.
        string prefix=contents[..first];string? face=null;bool bold=false,italic=false;
        for(int i=0;i<prefix.Length-1;i++)
        {
            if(prefix[i]!='\\')continue;char code=prefix[++i];
            if(code is not ('f' or 'F'))continue;
            int end=prefix.IndexOf(';',i+1);var values=prefix[(i+1)..end].Split('|');face=values[0];bold=false;italic=false;
            foreach(string value in values.Skip(1)){if(value=="b1")bold=true;else if(value=="b0")bold=false;else if(value=="i1")italic=true;else if(value=="i0")italic=false;}
            i=end;
        }
        return new(prefix,contents[last..],face,bold,italic);
    }
    public static string Replace(string contents,string[] expectedParagraphs,string[] paragraphs)
    {
        string expected=string.Join("\n",expectedParagraphs);
        if(!string.Equals(MTextContent.PlainText(contents),expected,StringComparison.Ordinal))
            throw new DomainException("TEXT_CONTENT_CHANGED","Visible MTEXT paragraphs differ from expected_paragraphs after preceding edits.");
        var format=Capture(contents);string encoded=MTextEditContent.Encode(paragraphs,true);
        string result=format.Prefix+encoded+format.Suffix;
        if(MTextContent.PlainText(result)!=string.Join("\n",paragraphs))
            throw new DomainException("UNSUPPORTED_MTEXT_CONTENT","The retained native formatting envelope cannot represent the replacement reversibly.");
        return result;
    }
}
