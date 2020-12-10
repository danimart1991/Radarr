using System.Collections.Generic;
using FluentValidation.Results;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Notifications.Emby
{
    public class MediaBrowser : NotificationBase<MediaBrowserSettings>
    {
        private readonly IMediaBrowserService _mediaBrowserService;

        public MediaBrowser(IMediaBrowserService mediaBrowserService)
        {
            _mediaBrowserService = mediaBrowserService;
        }

        public override string Link => "https://emby.media/";
        public override string Name => "Emby";

        public override void OnGrab(GrabMessage grabMessage)
        {
            if (Settings.Notify)
            {
                _mediaBrowserService.Notify(Settings, MOVIE_GRABBED_TITLE_BRANDED, grabMessage.Message);
            }
        }

        public override void OnDownload(DownloadMessage message)
        {
            if (Settings.Notify)
            {
                _mediaBrowserService.Notify(Settings, MOVIE_DOWNLOADED_TITLE_BRANDED, message.Message);
            }

            UpdateRefreshLibraryIsNeeded(message.Movie);
        }

        public override void OnMovieRename(Movie movie)
        {
            UpdateRefreshLibraryIsNeeded(movie);
        }

        public override void OnHealthIssue(HealthCheck.HealthCheck message)
        {
            if (Settings.Notify)
            {
                _mediaBrowserService.Notify(Settings, HEALTH_ISSUE_TITLE_BRANDED, message.Message);
            }
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            failures.AddIfNotNull(_mediaBrowserService.Test(Settings));

            return new ValidationResult(failures);
        }

        private void UpdateRefreshLibraryIsNeeded(Movie movie)
        {
            if (movie != null && Settings.UpdateLibraryMode > 0)
            {
                if (Settings.UpdateLibraryDelay > 0)
                {
                    var timeSpan = new TimeSpan(0, Settings.UpdateLibraryDelay, 0);
                    System.Threading.Thread.Sleep(timeSpan);
                }

                switch (Settings.UpdateLibraryMode)
                {
                    case 1:
                        _logger.Debug("{0} - Scheduling library update for created movie {1} {2}", Name, movie.Id, movie.Title);
                        _mediaBrowserService.UpdateMovies(Settings, movie, "Created");
                        break;
                    case 2:
                        _logger.Debug("{0} - Scheduling library refresh");
                        _mediaBrowserService.RefreshMovies(Settings);
                        break;
                }
            }
        }
    }
}
