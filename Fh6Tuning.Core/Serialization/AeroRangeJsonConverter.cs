using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fh6Tuning.Core.Serialization;

/// <summary>
/// Serializes <see cref="AeroRange"/> as a 2-element JSON array <c>[min, max]</c> with <c>null</c>
/// preserved, matching the legacy shape (<c>aeroFront: [122, 203]</c> / <c>[null, null]</c>) so the
/// parity harness can round-trip JS-produced input JSON into the input record.
/// </summary>
public sealed class AeroRangeJsonConverter : JsonConverter<AeroRange>
{
    public override AeroRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return AeroRange.None;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("AeroRange must be a JSON array [min, max] or null.");

        double? min = ReadElement(ref reader);
        double? max = ReadElement(ref reader);

        // consume the closing ]
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("AeroRange must be a 2-element array [min, max].");

        return new AeroRange(min, max);
    }

    private static double? ReadElement(ref Utf8JsonReader reader)
    {
        reader.Read();
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number => reader.GetDouble(),
            _ => throw new JsonException("AeroRange elements must be a number or null."),
        };
    }

    public override void Write(Utf8JsonWriter writer, AeroRange value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        WriteElement(writer, value.Min);
        WriteElement(writer, value.Max);
        writer.WriteEndArray();
    }

    private static void WriteElement(Utf8JsonWriter writer, double? v)
    {
        if (v is null) writer.WriteNullValue();
        else writer.WriteNumberValue(v.Value);
    }
}
