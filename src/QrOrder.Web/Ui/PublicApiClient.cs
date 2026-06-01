namespace QrOrder.Web.Ui
{
    public class PublicApiClient
    {
        private readonly HttpClient _http;

        public PublicApiClient(IHttpClientFactory factory)
        {
            _http = factory.CreateClient("samehost");
        }

        public Task<T?> GetAsync<T>(string url) => _http.GetFromJsonAsync<T>(url);
        public Task<HttpResponseMessage> PostAsync<T>(string url, T body) => _http.PostAsJsonAsync(url, body);
    }
}
