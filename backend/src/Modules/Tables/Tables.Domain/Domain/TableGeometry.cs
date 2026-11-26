using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoodVision.Tables.Domain;

[JsonConverter(typeof(TablePointJsonConverter))]
public sealed record TablePoint(double X, double Y);

public sealed class TablePointJsonConverter : JsonConverter<TablePoint>
{
    public override TablePoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read();
            var x = reader.GetDouble();
            reader.Read();
            var y = reader.GetDouble();
            reader.Read(); // EndArray
            return new TablePoint(x, y);
        }
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            double x = 0, y = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var prop = reader.GetString();
                    reader.Read();
                    if (string.Equals(prop, "x", StringComparison.OrdinalIgnoreCase)) x = reader.GetDouble();
                    else if (string.Equals(prop, "y", StringComparison.OrdinalIgnoreCase)) y = reader.GetDouble();
                }
            }
            return new TablePoint(x, y);
        }
        throw new JsonException("Expected array [x, y] or object {x, y}");
    }

    public override void Write(Utf8JsonWriter writer, TablePoint value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteEndArray();
    }
}

public sealed record TableGeometry(
    [property: JsonPropertyName("display_polygon")] IReadOnlyList<TablePoint> DisplayPolygon,
    [property: JsonPropertyName("counting_polygon")] IReadOnlyList<TablePoint> CountingPolygon)
{
    public static TableGeometry Create(
        IEnumerable<TablePoint> displayPolygon,
        IEnumerable<TablePoint> countingPolygon) =>
        new(displayPolygon.ToList().AsReadOnly(), countingPolygon.ToList().AsReadOnly());
}
