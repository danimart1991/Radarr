using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Notifications.Emby
{
    public class MediaBrowserProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public MediaBrowserProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void Notify(MediaBrowserSettings settings, string title, string message)
        {
            var path = "/Notifications/Admin";
            var request = BuildRequest(path, settings);
            request.Headers.ContentType = "application/json";

            request.SetContent(new
            {
                Name = title,
                Description = message,
                ImageUrl = "https://raw.github.com/Radarr/Radarr/develop/Logo/64.png"
            }.ToJson());

            ProcessPostRequest(request, settings);
        }

        public void UpdateMovies(MediaBrowserSettings settings, string moviePath, string updateType)
        {
            var path = "/Library/Media/Updated";
            var request = BuildRequest(path, settings);
            request.Headers.ContentType = "application/json";

            request.SetContent(new
            {
                Updates = new[]
                {
                    new
                    {
                        Path = moviePath,
                        UpdateType = updateType
                    }
                }
            }.ToJson());

            ProcessPostRequest(request, settings);
        }

        public void RefreshMovies(MediaBrowserSettings settings)
        {
            var path = "/Library/Refresh";
            var request = BuildRequest(path, settings);

            ProcessGetRequest(request, settings);
        }

        private string ProcessGetRequest(HttpRequest request, MediaBrowserSettings settings)
        {
            var response = _httpClient.Get(request);
            _logger.Trace("Response: {0}", response.Content);

            CheckForError(response);

            return response.Content;
        }

        private string ProcessPostRequest(HttpRequest request, MediaBrowserSettings settings)
        {
            var response = _httpClient.Post(request);
            _logger.Trace("Response: {0}", response.Content);

            CheckForError(response);

            return response.Content;
        }

        private HttpRequest BuildRequest(string path, MediaBrowserSettings settings)
        {
            var scheme = settings.UseSsl ? "https" : "http";
            var url = $@"{scheme}://{settings.Address}/mediabrowser";

            var request = new HttpRequestBuilder(url).Resource(path).Build();
            request.Headers.Add("X-MediaBrowser-Token", settings.ApiKey);

            return request;
        }

        private void CheckForError(HttpResponse response)
        {
            _logger.Debug("Looking for error in response: {0}", response);

            //TODO: actually check for the error
        }
    }
}
