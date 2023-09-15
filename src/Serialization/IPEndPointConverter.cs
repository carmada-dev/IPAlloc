using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IPAlloc.Serialization;

public sealed class IPEndPointConverter : JsonConverter<IPEndPoint>
{
    public override IPEndPoint? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return IPEndPoint.TryParse(reader.GetString() ?? string.Empty, out var ipEndPoint) ? ipEndPoint : default;
    }

    public override void Write(Utf8JsonWriter writer, IPEndPoint value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value?.ToString());
    }
}
