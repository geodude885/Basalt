using System.Text;
using System.Text.Json;

namespace BossBattles.Services.Charts;

/// <summary>
/// Edits a chart's text in place: one top-level property's value is replaced (or the property is
/// added), and every other character - comments, layout, the author's order - stays as written.
/// What the in-game tools change (<c>format</c>, <c>arena</c>, <c>lane</c>, <c>tracks</c>,
/// <c>end</c>) is written through this, so a hand-written chart survives being recorded into.
/// JSON with comments and trailing commas, as the loader reads it.
/// </summary>
public static class ChartDocument
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The chart text with top-level <paramref name="name"/> set to <paramref name="value"/> (serialised as JSON).</summary>
    public static string Set(string text, string name, object value) => SetRaw(text, name, JsonSerializer.Serialize(value, Indented));

    /// <summary>Where top-level <paramref name="name"/>'s value is in the text - for edits inside it - or false when absent.</summary>
    public static bool TryFindValue(string text, string name, out int start, out int end)
    {
        start = end = -1;
        var scanner = new Scanner(text);
        var open = scanner.SkipTrivia(0);
        if (open >= text.Length || text[open] != '{') return false;

        var at = open + 1;
        while (true)
        {
            at = scanner.SkipTrivia(at);
            if (at >= text.Length || text[at] == '}') return false;
            if (text[at] == ',')
            {
                at++;
                continue;
            }

            if (text[at] != '"') return false;
            var keyEnd = scanner.StringEnd(at);
            var key = JsonSerializer.Deserialize<string>(text[at..keyEnd]) ?? "";
            var colon = scanner.SkipTrivia(keyEnd);
            var valueStart = scanner.SkipTrivia(colon + 1);
            var valueEnd = scanner.ValueEnd(valueStart);

            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                start = valueStart;
                end = valueEnd;
                return true;
            }

            at = valueEnd;
        }
    }

    /// <summary>The chart text with top-level <paramref name="name"/> set to the JSON text <paramref name="json"/>.</summary>
    /// <exception cref="FormatException">If the text is not a JSON object.</exception>
    public static string SetRaw(string text, string name, string json)
    {
        var value = Reindent(json.Trim(), "  ");
        var scanner = new Scanner(text);

        var open = scanner.SkipTrivia(0);
        if (open >= text.Length || text[open] != '{') throw new FormatException("a chart is a JSON object: it must start with {");

        var at = open + 1;
        var lastValueEnd = -1;
        while (true)
        {
            at = scanner.SkipTrivia(at);
            if (at >= text.Length) throw new FormatException("the chart's { is never closed");

            var c = text[at];
            if (c == '}') return Insert(text, open, at, lastValueEnd, name, value, scanner);
            if (c == ',')
            {
                at++;
                continue;
            }

            if (c != '"') throw new FormatException($"expected a property name at character {at}");

            var keyEnd = scanner.StringEnd(at);
            var key = JsonSerializer.Deserialize<string>(text[at..keyEnd]) ?? "";

            var colon = scanner.SkipTrivia(keyEnd);
            if (colon >= text.Length || text[colon] != ':') throw new FormatException($"expected : after \"{key}\"");

            var valueStart = scanner.SkipTrivia(colon + 1);
            var valueEnd = scanner.ValueEnd(valueStart);

            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return text[..valueStart] + value + text[valueEnd..];

            lastValueEnd = valueEnd;
            at = valueEnd;
        }
    }

    /// <summary>A new last property, after a comma if the one before has none.</summary>
    private static string Insert(string text, int open, int close, int lastValueEnd, string name, string value, Scanner scanner)
    {
        var property = $"\n  {JsonSerializer.Serialize(name)}: {value}";

        if (lastValueEnd < 0) return text[..(open + 1)] + property + "\n" + text[close..];

        // After the last value: an existing trailing comma is kept, else one is added.
        var afterValue = scanner.SkipTrivia(lastValueEnd);
        var hasComma = afterValue < close && text[afterValue] == ',';
        var insertAt = hasComma ? afterValue + 1 : lastValueEnd;

        // Keep anything between the last value and the brace (a trailing comment) before the new property.
        var between = text[insertAt..close].TrimEnd();
        return text[..insertAt] + (hasComma ? "" : ",") + between + property + "\n" + text[close..];
    }

    private static string Reindent(string json, string indent)
    {
        var lines = json.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder(lines[0]);
        for (var i = 1; i < lines.Length; i++) builder.Append('\n').Append(indent).Append(lines[i]);
        return builder.ToString();
    }

    /// <summary>Just enough of a JSON-with-comments reader to find where things start and end.</summary>
    private sealed class Scanner
    {
        private readonly string _text;

        public Scanner(string text)
        {
            _text = text;
        }

        /// <summary>Past whitespace and comments.</summary>
        public int SkipTrivia(int at)
        {
            while (at < _text.Length)
            {
                if (char.IsWhiteSpace(_text[at])) at++;
                else if (Starts(at, "//")) at = LineEnd(at);
                else if (Starts(at, "/*")) at = BlockEnd(at);
                else break;
            }

            return at;
        }

        /// <summary>The index just past a string starting at <paramref name="at"/> (its opening quote).</summary>
        public int StringEnd(int at)
        {
            for (var i = at + 1; i < _text.Length; i++)
            {
                if (_text[i] == '\\') i++;
                else if (_text[i] == '"') return i + 1;
            }

            throw new FormatException($"a string starting at character {at} never ends");
        }

        /// <summary>The index just past the value starting at <paramref name="at"/>.</summary>
        public int ValueEnd(int at)
        {
            if (at >= _text.Length) throw new FormatException("a property has no value");

            var c = _text[at];
            if (c == '"') return StringEnd(at);
            if (c is not ('{' or '['))
            {
                var end = at;
                while (end < _text.Length && !char.IsWhiteSpace(_text[end]) && _text[end] is not (',' or '}' or ']') && !Starts(end, "//") && !Starts(end, "/*")) end++;
                return end;
            }

            var depth = 0;
            for (var i = at; i < _text.Length; i++)
            {
                var d = _text[i];
                if (d == '"')
                {
                    i = StringEnd(i) - 1;
                }
                else if (Starts(i, "//"))
                {
                    i = LineEnd(i) - 1;
                }
                else if (Starts(i, "/*"))
                {
                    i = BlockEnd(i) - 1;
                }
                else if (d is '{' or '[')
                {
                    depth++;
                }
                else if (d is '}' or ']')
                {
                    depth--;
                    if (depth == 0) return i + 1;
                }
            }

            throw new FormatException($"a value starting at character {at} is never closed");
        }

        private bool Starts(int at, string token) => string.CompareOrdinal(_text, at, token, 0, token.Length) == 0;

        private int LineEnd(int at)
        {
            var end = _text.IndexOf('\n', at);
            return end < 0 ? _text.Length : end + 1;
        }

        private int BlockEnd(int at)
        {
            var end = _text.IndexOf("*/", at + 2, StringComparison.Ordinal);
            return end < 0 ? _text.Length : end + 2;
        }
    }
}
