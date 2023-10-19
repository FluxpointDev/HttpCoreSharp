using Newtonsoft.Json;

namespace HttpCoreSharp
{
    public class HttpCoreOptions
    {
        public bool EndpointRequiresEndingSlash { get; set; }

        public bool ThrowGetRequest { get; set; }

        public bool CheckErrorResponse { get; set; }

        public JsonSerializer JsonSerializer { get; set; } = new JsonSerializer();

        public JsonSerializer JsonDeserializer { get; set; } = new JsonSerializer();

        public HttpClient Http = new HttpClient();
    }
}
