using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Civ2engine.SaveLoad;

/// <summary>
/// Reads a unit's ExtendedData dictionary, tolerating BOTH JSON shapes:
///  - the array-of-pairs form the custom save writer emits for Dictionary&lt;string,string&gt;
///    (<c>[{"Key":"horde","Value":"1"}]</c>), and
///  - the standard object form (<c>{"horde":"1"}</c>).
/// The writer (UTF8JsonWriterExtensions.WriteNonDefaultFields) treats a Dictionary as an
/// IEnumerable and writes it as an array, but System.Text.Json deserializes
/// Dictionary&lt;string,string&gt; from an object. Without this converter, any save containing
/// units with ExtendedData (e.g. barbarian "horde" units) fails to load with a JsonException.
/// </summary>
public class ExtendedDataConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string>? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.StartObject:
            {
                var result = new Dictionary<string, string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var key = reader.GetString()!;
                    reader.Read();
                    result[key] = reader.GetString();
                }

                return result;
            }

            case JsonTokenType.StartArray:
            {
                var result = new Dictionary<string, string>();
                // Each element is an object like {"Key":"horde","Value":"1"}
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    string? key = null, value = null;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        var prop = reader.GetString();
                        reader.Read();
                        if (string.Equals(prop, "Key", StringComparison.OrdinalIgnoreCase))
                            key = reader.GetString();
                        else if (string.Equals(prop, "Value", StringComparison.OrdinalIgnoreCase))
                            value = reader.GetString();
                    }

                    if (key != null)
                        result[key] = value;
                }

                return result;
            }

            default:
                throw new JsonException($"Unexpected token {reader.TokenType} when reading ExtendedData");
        }
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var kvp in value)
        {
            writer.WriteString(kvp.Key, kvp.Value);
        }

        writer.WriteEndObject();
    }
}
