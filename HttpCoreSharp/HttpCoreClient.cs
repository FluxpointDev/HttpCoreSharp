using System.Globalization;
using System.Text;
using Microsoft.IO;
using Newtonsoft.Json;
using static System.Net.WebRequestMethods;

namespace HttpCoreSharp
{
    public class HttpCoreClient<IRequestJson, IRequestError>
        where IRequestJson : class
        where IRequestError : IHttpCoreError
    {

        public HttpCoreClient(string hostUrl, HttpCoreOptions? options = null)
        {
            Options = options;
            if (Options == null)
                Options = new HttpCoreOptions();

            if (string.IsNullOrEmpty(hostUrl))
                throw new Exception("Client HostUrl can not be empty.");

            if (!Uri.IsWellFormedUriString(hostUrl, UriKind.Absolute))
                throw new Exception("Client HostUrl is an invalid format.");

            if (Options.Http == null)
                Options.Http = new HttpClient();

            HostUrl = hostUrl;

            if (!HostUrl.EndsWith('/'))
                HostUrl += "/";

            Options.Http.BaseAddress = new Uri(hostUrl);

            Http = Options.Http;
        }

        private HttpCoreOptions Options { get; set; }

        private HttpClient Http { get; set; }

        public string HostUrl { get; private set; }

        private static readonly RecyclableMemoryStreamManager recyclableMemoryStreamManager = new RecyclableMemoryStreamManager();

        public Task<TResponse?> GetAsync<TResponse>(string endpoint, IRequestJson? json = null) where TResponse : class
            => InternalJsonRequest<TResponse>(HttpMethod.Get, endpoint, json, Options.ThrowGetRequest);

        public Task<dynamic?> GetDynamicAsync(string endpoint, IRequestJson? json = null)
            => InternalJsonRequest<dynamic>(HttpMethod.Get, endpoint, json, Options.ThrowGetRequest);

        public Task<HttpResponseMessage> GetAsync(string endpoint, IRequestJson? json = null)
            => InternalResponseRequest(HttpMethod.Get, endpoint, json, Options.ThrowGetRequest);

        public Task<TResponse> PutAsync<TResponse>(string endpoint, IRequestJson? json = null) where TResponse : class
            => InternalJsonRequest<TResponse>(HttpMethod.Put, endpoint, json, Options.ThrowGetRequest)!;

        public Task<dynamic> PutDynamicAsync(string endpoint, IRequestJson? json = null)
            => InternalJsonRequest<dynamic>(HttpMethod.Put, endpoint, json, Options.ThrowGetRequest)!;

        public Task<HttpResponseMessage> PutAsync(string endpoint, IRequestJson? json = null)
            => InternalResponseRequest(HttpMethod.Put, endpoint, json, Options.ThrowGetRequest);

        public Task<TResponse> DeleteAsync<TResponse>(string endpoint, IRequestJson? json = null) where TResponse : class
            => InternalJsonRequest<TResponse>(HttpMethod.Delete, endpoint, json, Options.ThrowGetRequest)!;

        public Task<dynamic> DeleteDynamicAsync(string endpoint, IRequestJson? json = null)
            => InternalJsonRequest<dynamic>(HttpMethod.Delete, endpoint, json, Options.ThrowGetRequest)!;

        public Task<HttpResponseMessage> DeleteAsync(string endpoint, IRequestJson? json = null)
            => InternalResponseRequest(HttpMethod.Delete, endpoint, json, Options.ThrowGetRequest);

        public Task<TResponse> PatchAsync<TResponse>(string endpoint, IRequestJson? json = null) where TResponse : class
            => InternalJsonRequest<TResponse>(HttpMethod.Patch, endpoint, json, Options.ThrowGetRequest)!;

        public Task<dynamic> PatchDynamicAsync(string endpoint, IRequestJson? json = null)
            => InternalJsonRequest<dynamic>(HttpMethod.Patch, endpoint, json, Options.ThrowGetRequest)!;

        public Task<HttpResponseMessage> PatchAsync(string endpoint, IRequestJson? json = null)
            => InternalResponseRequest(HttpMethod.Patch, endpoint, json, Options.ThrowGetRequest);

        public Task<TResponse> PostAsync<TResponse>(string endpoint, IRequestJson? json = null) where TResponse : class
            => InternalJsonRequest<TResponse>(HttpMethod.Post, endpoint, json)!;

        public Task<dynamic> PostDynamicAsync(string endpoint, IRequestJson? json = null)
            => InternalJsonRequest<dynamic>(HttpMethod.Post, endpoint, json, Options.ThrowGetRequest)!;

        public Task<HttpResponseMessage> PostAsync(string endpoint, IRequestJson? json = null)
            => InternalResponseRequest(HttpMethod.Post, endpoint, json);

        private async Task<HttpResponseMessage> InternalResponseRequest(HttpMethod method, string endpoint, object? request, bool throwGetRequest = false)
        {
            var Req = await InternalRequest(method, endpoint, request, throwGetRequest);
            return Req;
        }

        private async Task<TResponse?> InternalJsonRequest<TResponse>(HttpMethod method, string endpoint, object? request, bool throwGetRequest = false)
            where TResponse : class
        {
            var Req = await InternalRequest(method, endpoint, request, throwGetRequest);
            TResponse? Response = default;
            if (Req.IsSuccessStatusCode)
            {
                int BufferSize = (int)Req.Content.Headers.ContentLength.GetValueOrDefault();
                try
                {
                    using (MemoryStream Stream = recyclableMemoryStreamManager.GetStream("HttpCore-SendRequest", BufferSize))
                    {
                        await Req.Content.CopyToAsync(Stream);
                        Stream.Position = 0;
                        Response = DeserializeJson<TResponse>(Stream);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to parse json response: " + ex.Message + " (500)");
                }
            }

            return Response;
        }


        private async Task<HttpResponseMessage> InternalRequest(HttpMethod method, string endpoint, object? request, bool throwRequest)

        {
            if (Options.EndpointRequiresEndingSlash && !endpoint.EndsWith('/'))
                endpoint += "/";

            if (endpoint.StartsWith('/'))
                endpoint = endpoint.Substring(1);

            HttpRequestMessage Mes = new HttpRequestMessage(method, new Uri(HostUrl + endpoint));

            if (request != null)
                Mes.Content = new StringContent(SerializeJson(request), Encoding.UTF8, "application/json");

            HttpResponseMessage Req = await Http.SendAsync(Mes);

            if (!Req.IsSuccessStatusCode)
            {
                if (Req.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
                    Req = await Http.SendAsync(Mes);

                if (!Req.IsSuccessStatusCode)
                {
                    IRequestError? Error = null;

                    if (Options.CheckErrorResponse && Req.Content.Headers.ContentLength.HasValue)
                    {
                        try
                        {
                            int BufferSize = (int)Req.Content.Headers.ContentLength.Value;
                            using (MemoryStream Stream = recyclableMemoryStreamManager.GetStream("HttpCore-SendRequest", BufferSize))
                            {
                                await Req.Content.CopyToAsync(Stream);
                                Stream.Position = 0;
                                Error = DeserializeJson<IRequestError>(Stream);
                            }
                        }
                        catch { }
                    }

                    if (throwRequest)
                    {
                        if (Error != null)
                            throw new Exception(Error.ErrorMessage + $" ({(int)Req.StatusCode})");
                        else
                            throw new Exception(Req.ReasonPhrase + $" ({(int)Req.StatusCode})");
                    }

                    if (Error != null)
                        Req.ReasonPhrase = Error.ErrorMessage;

                    if (string.IsNullOrEmpty(Req.ReasonPhrase))
                        Req.ReasonPhrase = $"Unknown error, response code {(int)Req.StatusCode}.";
                }
            }


            return Req;
        }

        private string SerializeJson(object value)
        {
            StringBuilder sb = new StringBuilder(256);
            using (TextWriter text = new StringWriter(sb, CultureInfo.InvariantCulture))
            using (JsonWriter writer = new JsonTextWriter(text))
                Options.JsonSerializer.Serialize(writer, value);
            return sb.ToString();
        }

        private T? DeserializeJson<T>(MemoryStream jsonStream)
        {
            using (TextReader text = new StreamReader(jsonStream))
            using (JsonReader reader = new JsonTextReader(text))
                return Options.JsonDeserializer.Deserialize<T>(reader);
        }
    }

    

    public class IHttpCoreError
    {
        public virtual string ErrorMessage { get; set; }
    }
}
