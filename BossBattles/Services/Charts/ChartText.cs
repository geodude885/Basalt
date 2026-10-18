using System.Text.Json;

namespace BossBattles.Services.Charts;

// Edits a chart's text in place: one top-level property's value is replaced or added, and every other character,
// comments included, stays as written.
public static class ChartText
{
    // Where a top-level property's value is in the text, for edits inside it; null when absent or unreadable.
    public static Range? FindValue(string text, string name)
    {
        try
        {
            return FindProperty(text, name).Value;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // The text with a top-level property's value changed by `edit`; unchanged when there is no such property.
    public static string EditValue(string text, string name, Func<string, string> edit) =>
        FindValue(text, name) is { } value ? text[..value.Start] + edit(text[value]) + text[value.End..] : text;

    // The text with a top-level property set to `json`: replaced where it is, else added after the last property.
    public static string Set(string text, string name, string json)
    {
        var value = string.Join("\n  ", json.Trim().Replace("\r\n", "\n").Split('\n'));
        (var existing, var lastValueEnd, var openBrace, var closeBrace) = FindProperty(text, name);
        if (existing is { } replaced) return text[..replaced.Start] + value + text[replaced.End..];

        var newProperty = $"\n  {JsonSerializer.Serialize(name)}: {value}";
        if (lastValueEnd < 0) return text[..(openBrace + 1)] + newProperty + "\n" + text[closeBrace..];

        // After the last value: its trailing comma kept, else one added. Anything between it and the brace (a
        // trailing comment) stays, before the new property.
        var afterLastValue = new Scanner(text).SkipTrivia(lastValueEnd);
        var hasComma = afterLastValue < closeBrace && text[afterLastValue] == ',';
        var insertAt = hasComma ? afterLastValue + 1 : lastValueEnd;
        var keptBeforeBrace = text[insertAt..closeBrace].TrimEnd();
        return text[..insertAt] + (hasComma ? "" : ",") + keptBeforeBrace + newProperty + "\n" + text[closeBrace..];
    }

    // Walks the top-level object's properties until `name`. Without it, says where the last value ends and the
    // object's braces are, for adding one.
    private static (Range? Value, int LastValueEnd, int OpenBrace, int CloseBrace) FindProperty(string text, string name)
    {
        var scanner = new Scanner(text);
        var openBrace = scanner.SkipTrivia(0);
        if (openBrace >= text.Length || text[openBrace] != '{') throw new FormatException("a chart is a JSON object: it must start with {");

        var lastValueEnd = -1;
        var at = openBrace + 1;
        while (true)
        {
            at = scanner.SkipTrivia(at);
            if (at >= text.Length) throw new FormatException("the chart's { is never closed");
            if (text[at] == '}') return (null, lastValueEnd, openBrace, at);
            if (text[at] == ',')
            {
                at++;
                continue;
            }

            if (text[at] != '"') throw new FormatException($"expected a property name at character {at}");
            var keyEnd = scanner.StringEnd(at);
            var key = JsonSerializer.Deserialize<string>(text[at..keyEnd]) ?? "";
            var colon = scanner.SkipTrivia(keyEnd);
            if (colon >= text.Length || text[colon] != ':') throw new FormatException($"expected : after \"{key}\"");

            var valueStart = scanner.SkipTrivia(colon + 1);
            var valueEnd = scanner.ValueEnd(valueStart);
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return (valueStart..valueEnd, lastValueEnd, openBrace, at);

            lastValueEnd = valueEnd;
            at = valueEnd;
        }
    }

    // Positions in JSON with comments (// and /* */), which System.Text.Json reads but cannot write back.
    private class Scanner(string text)
    {
        // Past whitespace and comments.
        public int SkipTrivia(int at)
        {
            while (at < text.Length)
            {
                if (char.IsWhiteSpace(text[at])) at++;
                else if (Starts(at, "//")) at = LineEnd(at);
                else if (Starts(at, "/*")) at = BlockEnd(at);
                else break;
            }

            return at;
        }

        // Just past a string whose opening quote is at `at`.
        public int StringEnd(int at)
        {
            for (var i = at + 1; i < text.Length; i++)
            {
                if (text[i] == '\\') i++;
                else if (text[i] == '"') return i + 1;
            }

            throw new FormatException($"a string starting at character {at} never ends");
        }

        // Just past the value starting at `at`: a string, a number or word, or a whole object or array.
        public int ValueEnd(int at)
        {
            if (at >= text.Length) throw new FormatException("a property has no value");

            var first = text[at];
            if (first == '"') return StringEnd(at);
            if (first is not ('{' or '['))
            {
                var end = at;
                while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not (',' or '}' or ']') && !Starts(end, "//") && !Starts(end, "/*")) end++;
                return end;
            }

            var depth = 0;
            for (var i = at; i < text.Length; i++)
            {
                if (text[i] == '"') i = StringEnd(i) - 1;
                else if (Starts(i, "//")) i = LineEnd(i) - 1;
                else if (Starts(i, "/*")) i = BlockEnd(i) - 1;
                else if (text[i] is '{' or '[') depth++;
                else if (text[i] is '}' or ']' && --depth == 0) return i + 1;
            }

            throw new FormatException($"a value starting at character {at} is never closed");
        }

        private bool Starts(int at, string token) => string.CompareOrdinal(text, at, token, 0, token.Length) == 0;

        private int LineEnd(int at)
        {
            var end = text.IndexOf('\n', at);
            return end < 0 ? text.Length : end + 1;
        }

        private int BlockEnd(int at)
        {
            var end = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
            return end < 0 ? text.Length : end + 2;
        }
    }
}
