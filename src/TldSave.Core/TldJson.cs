using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TldSave.Core;

/// <summary>
/// JSON helpers tuned for TLD saves:
/// - the game writes non-standard NaN / Infinity / -Infinity, which must be written back as bare symbols
/// - date-like strings (e.g. m_Timestamp) must stay strings, not be converted to DateTime
/// - many fields are JSON strings that contain more JSON ("nested" fields)
/// </summary>
public static class TldJson
{
    public const int MaxDepth = 256;

    public static JToken Parse(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
            // Real saves nest at most ~13 levels; the limit turns a crafted, absurdly deep file into a clean
            // error instead of a stack overflow (which can't be caught) in the recursive code that walks it
            MaxDepth = TldJson.MaxDepth,
        };
        var token = JToken.ReadFrom(reader, new JsonLoadSettings
        {
            // Duplicate keys otherwise silently replace earlier game data when a JObject is loaded.
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        });

        // ReadFrom stops at the end of the first value. Consume the rest so a truncated/concatenated
        // document cannot appear valid simply because its first object happened to parse.
        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.Comment)
                throw new JsonReaderException("Unexpected content after the JSON value.");
        }
        return token;
    }

    public static string Serialize(JToken token, bool indented = false)
    {
        using var stringWriter = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = new JsonTextWriter(stringWriter)
        {
            Formatting = indented ? Formatting.Indented : Formatting.None,
            FloatFormatHandling = FloatFormatHandling.Symbol,
        })
        {
            token.WriteTo(writer);
        }
        return stringWriter.ToString();
    }

    /// <summary>Reads a field whose value is a JSON string, and parses it. Null if missing or empty.</summary>
    public static JToken? ReadNested(this JObject obj, string field)
    {
        var text = obj[field]?.Type == JTokenType.String ? (string?)obj[field] : null;
        return string.IsNullOrEmpty(text) ? null : Parse(text);
    }

    /// <summary>Writes a value back into a field as a JSON string.</summary>
    public static void WriteNested(this JObject obj, string field, JToken value)
    {
        obj[field] = Serialize(value);
    }

    /// <summary>Returns a copy where every nested JSON string is parsed, recursively. For display only.</summary>
    public static JToken Expand(JToken token)
    {
        switch (token)
        {
            case JObject obj:
                var expandedObj = new JObject();
                foreach (var property in obj.Properties())
                    expandedObj.Add(property.Name, Expand(property.Value));
                return expandedObj;

            case JArray array:
                return new JArray(array.Select(Expand));

            case JValue { Type: JTokenType.String } value:
                var text = (string?)value;
                if (text is { Length: > 1 } && (text[0] == '{' || text[0] == '['))
                {
                    try { return Expand(Parse(text)); }
                    catch (JsonException) { /* not JSON after all */ }
                }
                return value.DeepClone();

            default:
                return token.DeepClone();
        }
    }
}
