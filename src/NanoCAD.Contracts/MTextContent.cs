using System.Globalization;
using System.Text;

namespace NanoCAD.Contracts;

/// <summary>Conservative plain-text decoding; never evaluates fields or invokes a CAD API.</summary>
public static class MTextContent
{
    // Supports an explicit subset. Unknown/malformed formatting yields null, not
    // text with silently dropped content. Raw Contents remains available in the DTO.
    // Syntax references: ezdxf documentation, dxfentities/mtext.html and
    // dxfinternals/entities/mtext.html (linked in docs/phase1-contract.md).
    public static string? PlainText(string contents)
    {
        if (contents.Length > 16384 || contents.Contains("%<", StringComparison.Ordinal)) return null;
        var output = new StringBuilder(contents.Length);
        int depth = 0;
        for (int i = 0; i < contents.Length; i++)
        {
            char c = contents[i];
            if (c == '{') { if (++depth > 8) return null; continue; }
            if (c == '}') { if (--depth < 0) return null; continue; }
            if (c == '\r') continue;
            if (c == '%' && i + 1 < contents.Length && contents[i + 1] == '%') return null;
            if (c == '^')
            {
                if (++i == contents.Length) return null;
                switch (contents[i])
                {
                    case 'I': output.Append('\t'); break;
                    case 'J': output.Append('\n'); break;
                    case 'M': break;
                    case ' ': output.Append('^'); break;
                    default: return null;
                }
                continue;
            }
            if (c != '\\') { output.Append(c); continue; }
            if (++i == contents.Length) return null;
            char code = contents[i];
            switch (code)
            {
                case '\\': case '{': case '}': output.Append(code); break;
                case 'P': output.Append('\n'); break;
                case '~': output.Append('\u00a0'); break;
                case 'L': case 'l': case 'O': case 'o': case 'K': case 'k': break;
                case 'U':
                    if (i + 5 >= contents.Length || contents[i + 1] != '+' ||
                        !ushort.TryParse(contents.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier,
                            CultureInfo.InvariantCulture, out ushort unicode)) return null;
                    output.Append((char)unicode);
                    i += 5;
                    break;
                case 'C': case 'c': case 'A': case 'H': case 'W': case 'T': case 'Q':
                case 'F': case 'f': case 'p':
                    int end = contents.IndexOf(';', i + 1);
                    if (end < 0) return null;
                    string argument = contents[(i + 1)..end];
                    if (argument.Length == 0 || argument.IndexOfAny(new[] { '\\', '{', '}', '\r', '\n' }) >= 0)
                        return null;
                    // Optional terminators and malformed numeric arguments have
                    // host-specific rendering semantics; only accept canonical input.
                    if (code is 'C' or 'c')
                    {
                        if (argument.Any(ch => ch < '0' || ch > '9')) return null;
                    }
                    else if (code == 'A')
                    {
                        if (argument is not ("0" or "1" or "2")) return null;
                    }
                    else if (code is 'H' or 'W' or 'T' or 'Q')
                    {
                        string number = code != 'Q' && argument.EndsWith('x') ? argument[..^1] : argument;
                        if (!double.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint |
                            NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)) return null;
                    }
                    i = end;
                    break;
                // Fractions, field expressions and unsupported escapes need an
                // explicit representation policy; do not guess their visible text.
                default: return null;
            }
        }
        if (depth != 0) return null;
        return output.ToString();
    }
}
