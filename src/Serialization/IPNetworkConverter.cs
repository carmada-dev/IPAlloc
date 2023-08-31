using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace IPAlloc.Serialization
{
    public sealed class IPNetworkConverter : JsonConverter<IPNetwork>
    {
        public override IPNetwork? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return IPNetwork.TryParse(reader.GetString() ?? string.Empty, out var ipNetwork) ? ipNetwork : default;
        }

        public override void Write(Utf8JsonWriter writer, IPNetwork value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value?.ToString());
        }
    }
}
