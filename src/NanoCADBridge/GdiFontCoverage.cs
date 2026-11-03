using System.Runtime.InteropServices;
using System.Text;
using NanoCAD.Contracts;
namespace NanoCADBridge;
internal static class GdiFontCoverage
{
    public static void Require(string face,bool bold,bool italic,string text)
    {
        if(string.IsNullOrWhiteSpace(face))return; // SHX has no GDI cmap; do not invent a verdict.
        if(text.Any(char.IsSurrogate))throw new DomainException("FONT_CHECK_UNAVAILABLE","This font coverage check cannot verify supplementary Unicode characters; coverage is unknown, not missing.");
        var chars=new string(text.Where(c=>!char.IsControl(c)).Distinct().ToArray());
        IntPtr dc=CreateCompatibleDC(IntPtr.Zero),font=IntPtr.Zero,old=IntPtr.Zero;
        try {
            font=CreateFontW(24,0,0,0,bold?700:400,italic?1u:0u,0,0,1,0,0,0,0,face);
            if(dc==IntPtr.Zero||font==IntPtr.Zero)throw new DomainException("FONT_CHECK_UNAVAILABLE","Cannot inspect installed font coverage.");
            old=SelectObject(dc,font);var actual=new StringBuilder(256);GetTextFaceW(dc,256,actual);
            if(!string.Equals(actual.ToString(),face,StringComparison.OrdinalIgnoreCase))throw new DomainException("FONT_UNAVAILABLE","Requested font is not installed; Windows substituted another family.",false,new{requested=face,actual=actual.ToString()});
            var glyphs=new ushort[chars.Length];
            if(chars.Length>0&&GetGlyphIndicesW(dc,chars,chars.Length,glyphs,1)==uint.MaxValue)throw new DomainException("FONT_CHECK_UNAVAILABLE","Cannot read installed font glyphs.");
            var missing=chars.Where((c,i)=>glyphs[i]==65535).Select(c=>"U+"+((int)c).ToString("X4")).ToArray();
            if(missing.Length>0)throw new DomainException("FONT_GLYPHS_MISSING","Selected font cannot display required characters. Choose a compatible installed font before writing; no text was changed.",false,new{typeface=face,missing_codepoints=missing});
        } finally {if(old!=IntPtr.Zero)SelectObject(dc,old);if(font!=IntPtr.Zero)DeleteObject(font);if(dc!=IntPtr.Zero)DeleteDC(dc);}
    }
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr h);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateFontW(int h,int w,int e,int o,int weight,uint italic,uint underline,uint strike,uint charset,uint output,uint clip,uint quality,uint family,string face);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr h,IntPtr o);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] static extern uint GetGlyphIndicesW(IntPtr h,string text,int count,ushort[] glyphs,uint flags);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] static extern int GetTextFaceW(IntPtr h,int count,StringBuilder name);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr h);
}
