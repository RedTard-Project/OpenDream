using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Web;

namespace OpenDreamShared.Dream;

public static class ParamsHelpers {
    /// <summary>
    /// Enumerates every key and value in a list2params string
    /// </summary>
    /// <returns>Each key in the list, and a <see cref="string"/> for a single value or <see cref="IEnumerable{string}"/> for multiple values</returns>
    public static IEnumerable<KeyValuePair<string, object>> EnumerateParams2List(string queryString) {
        queryString = queryString.Replace(";", "&");
        NameValueCollection query = HttpUtility.ParseQueryString(queryString);

        foreach (string? queryKey in query.AllKeys) {
            string[]? queryValues = query.GetValues(queryKey);

            if (queryValues == null)
                continue;

            if (queryKey == null) { // queryValues contains every value without a key
                foreach (string value in queryValues.Distinct()) {
                    int count = queryValues.Count(item => item == value);

                    if (count > 1) { // "a;a;a" creates list(a=list("","",""))
                        yield return new(value, Enumerable.Repeat(string.Empty, count));
                    } else {
                        yield return new(value, string.Empty);
                    }
                }
            } else {
                yield return new(queryKey, queryValues.Length > 1 ? queryValues : queryValues[0]);
            }
        }
    }

    /// <summary>
    /// A single "key=value" of a winset
    /// </summary>
    /// <param name="Quoted">Whether the value was surrounded by quotes. They're not included in the value.</param>
    public readonly record struct WinsetParam(string Key, string Value, bool Quoted) {
        /// <summary>
        /// Whether the value is a "condition?true-statements:false-statements"
        /// </summary>
        public bool IsTernary {
            get {
                int ternary = Quoted ? -1 : Value.IndexOf('?');

                return ternary != -1 && Value.IndexOf('=', ternary) != -1;
            }
        }

        /// <returns>This param as it'd be written in a DMF file</returns>
        public string AsDMF() {
            if (IsTernary)
                return $"{Key}={Value}";

            var escaped = Value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");
            return $"{Key}=\"{escaped}\"";
        }
    }

    /// <summary>
    /// Enumerates every param in a winset.
    /// The same format as list2params, but a value can also be a quoted string with escape codes.
    /// </summary>
    public static IEnumerable<WinsetParam> EnumerateWinsetParams(string winsetParams) {
        int pos = 0;

        while (pos < winsetParams.Length) {
            int keyEnd = winsetParams.IndexOfAny(['=', ';', '&'], pos);
            if (keyEnd == -1)
                keyEnd = winsetParams.Length;

            var key = HttpUtility.UrlDecode(winsetParams[pos..keyEnd]).Trim();
            pos = keyEnd + 1;

            if (keyEnd == winsetParams.Length || winsetParams[keyEnd] != '=') { // No value
                if (key != string.Empty)
                    yield return new(key, string.Empty, false);

                continue;
            }

            while (pos < winsetParams.Length && char.IsWhiteSpace(winsetParams[pos]))
                pos++;

            if (TryReadQuotedValue(winsetParams, ref pos, out var quotedValue)) {
                yield return new(key, quotedValue, true);
                continue;
            }

            int valueEnd = winsetParams.IndexOfAny([';', '&'], pos);
            if (valueEnd == -1)
                valueEnd = winsetParams.Length;

            yield return new(key, HttpUtility.UrlDecode(winsetParams[pos..valueEnd].Trim()), false);
            pos = valueEnd + 1;
        }
    }

    /// <summary>
    /// Reads a quoted value and the separator after it. Nothing is read if the value isn't entirely a quoted string.
    /// </summary>
    private static bool TryReadQuotedValue(string text, ref int pos, out string value) {
        value = string.Empty;
        if (pos >= text.Length || text[pos] != '"')
            return false;

        var builder = new StringBuilder();
        int current = pos + 1;
        for (; current < text.Length && text[current] != '"'; current++) {
            if (text[current] != '\\' || current + 1 >= text.Length) {
                builder.Append(text[current]);
                continue;
            }

            current++;
            builder.Append(text[current] switch {
                'n' => '\n',
                't' => '\t',
                var escaped => escaped
            });
        }

        if (current >= text.Length) // No closing quote
            return false;

        do {
            current++;
        } while (current < text.Length && char.IsWhiteSpace(text[current]));

        if (current < text.Length && text[current] != ';' && text[current] != '&')
            return false;

        value = builder.ToString();
        pos = current + 1;
        return true;
    }
}
