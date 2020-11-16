using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using TMDbLib.Client;
using TMDbLib.Objects.Collections;
using TMDbLibMovies = TMDbLib.Objects.Movies;

namespace NzbDrone.Core.Extras.Metadata.Consumers.XbmcTmdb
{
    public class XbmcTmdbMetadata : MetadataBase<XbmcTmdbMetadataSettings>
    {
        private const string BaseImageUrl = "https://image.tmdb.org/t/p";

        private static readonly Regex MovieImagesRegex = new Regex(@"^(?<type>poster|banner|fanart|clearart|discart|keyart|landscape|logo|backdrop|clearlogo)\.(?:png|jpe?g)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MovieFileImageRegex = new Regex(@"(?<type>-thumb|-poster|-banner|-fanart|-clearart|-discart|-keyart|-landscape|-logo|-backdrop|-clearlogo)\.(?:png|jpe?g)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static TMDbClient TmdbClient;

        private readonly Logger _logger;
        private readonly IDetectXbmcTmdbNfo _detectNfo;

        public XbmcTmdbMetadata(
            IDetectXbmcTmdbNfo detectNfo,
            Logger logger)
        {
            _logger = logger;
            _detectNfo = detectNfo;
        }

        public override string Name => "Kodi (XBMC) / Emby from TMDB";

        public override string GetFilenameAfterMove(Movie movie, MovieFile movieFile, MetadataFile metadataFile)
        {
            _logger.Debug("Getting Movie Filename after move for: {0}", Path.Combine(movie.Path, movieFile.RelativePath));

            var movieFilePath = Path.Combine(movie.Path, movieFile.RelativePath);

            if (metadataFile.Type == MetadataType.MovieMetadata)
            {
                return GetMovieMetadataFilename(movieFilePath);
            }

            if (metadataFile.Type == MetadataType.MovieImage)
            {
                var isFanart = metadataFile.RelativePath.Contains("fanart.jpg");
                return Path.Combine(movie.Path, GetMovieImageFilename(movieFilePath, isFanart));
            }

            _logger.Debug("Unknown movie file metadata: {0}", metadataFile.RelativePath);
            return Path.Combine(movie.Path, metadataFile.RelativePath);
        }

        public override MetadataFile FindMetadataFile(Movie movie, string path)
        {
            var filename = Path.GetFileName(path);

            if (filename == null)
            {
                return null;
            }

            var metadata = new MetadataFile
            {
                MovieId = movie.Id,
                Consumer = GetType().Name,
                RelativePath = movie.Path.GetRelativePath(path)
            };

            _logger.Debug("Finding Movie Metadata file for: {0}", Path.Combine(movie.Path, metadata.RelativePath));

            if (MovieImagesRegex.IsMatch(filename))
            {
                metadata.Type = MetadataType.MovieImage;
                return metadata;
            }

            if (MovieFileImageRegex.IsMatch(filename))
            {
                metadata.Type = MetadataType.MovieImage;
                return metadata;
            }

            if (filename.Equals("movie.nfo", StringComparison.OrdinalIgnoreCase) &&
                _detectNfo.IsXbmcTmdbNfoFile(path))
            {
                metadata.Type = MetadataType.MovieMetadata;
                return metadata;
            }

            var parseResult = Parser.Parser.ParseMovieTitle(filename);

            if (parseResult != null &&
                Path.GetExtension(filename).Equals(".nfo", StringComparison.OrdinalIgnoreCase) &&
                _detectNfo.IsXbmcTmdbNfoFile(path))
            {
                metadata.Type = MetadataType.MovieMetadata;
                return metadata;
            }

            return null;
        }

        public override MetadataFileResult MovieMetadata(Movie movie, MovieFile movieFile)
        {
            var xmlResult = string.Empty;
            if (Settings.MovieMetadata)
            {
                _logger.Debug("Generating Movie Metadata for: {0}", Path.Combine(movie.Path, movieFile.RelativePath));

                if (TmdbClient == null)
                {
                    TmdbClient = new TMDbClient(Settings.ApiKey);
                }

                var movieMethods =
                    TMDbLibMovies.MovieMethods.Credits |
                    TMDbLibMovies.MovieMethods.Releases |
                    TMDbLibMovies.MovieMethods.Videos;

                var movieMetadataLanguage = (Settings.MovieMetadataLanguage == (int)Language.Original) ? (int)movie.OriginalLanguage : Settings.MovieMetadataLanguage;
                var selectedSettingsLanguage = Language.FindById(movieMetadataLanguage);
                var isoLanguage = IsoLanguages.Get(selectedSettingsLanguage);
                var originalIsoLanguage = IsoLanguages.Get(selectedSettingsLanguage);

                var tmdbMovie = TmdbClient.GetMovieAsync(movie.TmdbId, isoLanguage.TwoLetterCode, movieMethods).Result;

                Collection tmdbMovieCollection = null;
                if (tmdbMovie.BelongsToCollection != null)
                {
                    tmdbMovieCollection = TmdbClient.GetCollectionAsync(tmdbMovie.BelongsToCollection.Id, isoLanguage.TwoLetterCode).Result;
                }

                // TODO: Move this to the main call when TMDBLib updates to 1.7 with TMDbLibMovies.MovieMethods.Images
                var tmdbMovieImages = TmdbClient.GetMovieImagesAsync(movie.TmdbId, isoLanguage.TwoLetterCode, originalIsoLanguage.TwoLetterCode + ",en,null").Result;

                var sb = new StringBuilder();
                var xws = new XmlWriterSettings
                {
                    OmitXmlDeclaration = false,
                    Encoding = Encoding.UTF8,
                    Indent = false
                };

                using (var xw = XmlWriter.Create(sb, xws))
                {
                    var doc = new XDocument();

                    var details = new XElement("movie");

                    details.Add(new XElement("title", tmdbMovie.Title));

                    details.Add(new XElement("originaltitle", tmdbMovie.OriginalTitle));

                    details.Add(new XElement("sorttitle", tmdbMovie.Title.ToLowerInvariant()));

                    if (tmdbMovie.VoteAverage > 0)
                    {
                        var setRating = new XElement("ratings");
                        var setRatethemoviedb = new XElement("rating", new XAttribute("name", "themoviedb"), new XAttribute("max", "10"), new XAttribute("default", "true"));
                        setRatethemoviedb.Add(new XElement("value", tmdbMovie.VoteAverage));
                        setRatethemoviedb.Add(new XElement("votes", tmdbMovie.VoteCount));
                        setRating.Add(setRatethemoviedb);
                        details.Add(setRating);
                    }

                    details.Add(new XElement("rating", tmdbMovie.VoteAverage));

                    details.Add(new XElement("top250"));

                    var outlineEndPosition = tmdbMovie.Overview.IndexOf(". ");
                    details.Add(new XElement("outline", tmdbMovie.Overview.Substring(0, outlineEndPosition > 0 ? outlineEndPosition + 1 : tmdbMovie.Overview.Length)));

                    details.Add(new XElement("plot", tmdbMovie.Overview));

                    details.Add(new XElement("tagline", tmdbMovie.Tagline));

                    details.Add(new XElement("runtime", tmdbMovie.Runtime));

                    if (tmdbMovie.PosterPath != null)
                    {
                        details.Add(new XElement("thumb", new XAttribute("aspect", "poster"), new XAttribute("preview", BaseImageUrl + "/w185" + tmdbMovie.PosterPath), BaseImageUrl + "/original" + tmdbMovie.PosterPath));
                    }

                    if (tmdbMovieImages != null)
                    {
                        foreach (var poster in tmdbMovieImages.Posters)
                        {
                            details.Add(new XElement("thumb", new XAttribute("aspect", "poster"), new XAttribute("preview", BaseImageUrl + "/w185" + poster.FilePath), BaseImageUrl + "/original" + poster.FilePath));
                        }

                        if (tmdbMovieImages.Backdrops.Any())
                        {
                            var fanartElement = new XElement("fanart");

                            foreach (var poster in tmdbMovieImages.Backdrops)
                            {
                                fanartElement.Add(new XElement("thumb", new XAttribute("preview", BaseImageUrl + "/w300" + poster.FilePath), BaseImageUrl + "/original" + poster.FilePath));
                            }

                            details.Add(fanartElement);
                        }
                    }

                    if (tmdbMovie.Releases?.Countries != null)
                    {
                        var certification = tmdbMovie.Releases.Countries.FirstOrDefault(country => country.Iso_3166_1 == isoLanguage.TwoLetterCode.ToUpperInvariant())?.Certification;

                        if (certification == null)
                        {
                            certification = tmdbMovie.Releases.Countries.FirstOrDefault(country => country.Iso_3166_1 == "US")?.Certification;
                        }

                        details.Add(new XElement("mpaa", certification));
                    }

                    var uniqueId = new XElement("uniqueid", tmdbMovie.Id);
                    uniqueId.SetAttributeValue("type", "tmdb");
                    uniqueId.SetAttributeValue("default", true);
                    details.Add(uniqueId);
                    details.Add(new XElement("tmdbid", tmdbMovie.Id));

                    if (tmdbMovie.ImdbId.IsNotNullOrWhiteSpace())
                    {
                        var imdbId = new XElement("uniqueid", tmdbMovie.ImdbId);
                        imdbId.SetAttributeValue("type", "imdb");
                        details.Add(imdbId);
                        details.Add(new XElement("imdbid", tmdbMovie.ImdbId));
                    }

                    foreach (var genre in tmdbMovie.Genres)
                    {
                        details.Add(new XElement("genre", genre.Name));
                    }

                    foreach (var country in tmdbMovie.ProductionCountries)
                    {
                        details.Add(new XElement("country", country.Name));
                    }

                    if (tmdbMovieCollection != null)
                    {
                        details.Add(new XElement("collectionnumber", tmdbMovieCollection.Id));

                        var setElement = new XElement("set");
                        setElement.SetAttributeValue("tmdbcolid", tmdbMovieCollection.Id);

                        setElement.Add(new XElement("name", tmdbMovieCollection.Name));
                        setElement.Add(new XElement("overview", tmdbMovieCollection.Overview));

                        details.Add(setElement);
                    }

                    foreach (var writer in tmdbMovie.Credits.Crew.Where(crew => (crew.Job == "Screenplay" || crew.Job == "Story" || crew.Job == "Novel" || crew.Job == "Writer") && crew.Name.IsNotNullOrWhiteSpace()))
                    {
                        details.Add(new XElement("credits", writer.Name));
                    }

                    foreach (var director in tmdbMovie.Credits.Crew.Where(crew => crew.Job == "Director" && crew.Name.IsNotNullOrWhiteSpace()))
                    {
                        details.Add(new XElement("director", director.Name));
                    }

                    if (tmdbMovie.ReleaseDate.HasValue)
                    {
                        details.Add(new XElement("releasedate", tmdbMovie.ReleaseDate.Value.ToString("yyyy-MM-dd")));
                        details.Add(new XElement("premiered", tmdbMovie.ReleaseDate.Value.ToString("yyyy-MM-dd")));
                        details.Add(new XElement("year", tmdbMovie.ReleaseDate.Value.Year));
                    }

                    details.Add(new XElement("status", tmdbMovie.Status));

                    foreach (var company in tmdbMovie.ProductionCompanies)
                    {
                        details.Add(new XElement("studio", company.Name));
                    }

                    details.Add(new XElement("trailer", GetYoutubeTrailer(tmdbMovie)));

                    if (movieFile.MediaInfo != null)
                    {
                        var sceneName = movieFile.GetSceneOrFileName();

                        var fileInfo = new XElement("fileinfo");
                        var streamDetails = new XElement("streamdetails");

                        var video = new XElement("video");
                        video.Add(new XElement("aspect", movieFile.MediaInfo.Width / movieFile.MediaInfo.Height));
                        video.Add(new XElement("bitrate", movieFile.MediaInfo.VideoBitrate));
                        video.Add(new XElement("codec", MediaInfoFormatter.FormatVideoCodec(movieFile.MediaInfo, sceneName)));
                        video.Add(new XElement("framerate", movieFile.MediaInfo.VideoFps));
                        video.Add(new XElement("height", movieFile.MediaInfo.Height));
                        video.Add(new XElement("scantype", movieFile.MediaInfo.ScanType));
                        video.Add(new XElement("width", movieFile.MediaInfo.Width));

                        if (movieFile.MediaInfo.RunTime != default)
                        {
                            video.Add(new XElement("duration", movieFile.MediaInfo.RunTime.TotalMinutes));
                            video.Add(new XElement("durationinseconds", movieFile.MediaInfo.RunTime.TotalSeconds));
                        }

                        streamDetails.Add(video);

                        var audio = new XElement("audio");
                        var audioChannelCount = movieFile.MediaInfo.AudioChannelsStream > 0 ? movieFile.MediaInfo.AudioChannelsStream : movieFile.MediaInfo.AudioChannelsContainer;
                        audio.Add(new XElement("bitrate", movieFile.MediaInfo.AudioBitrate));
                        audio.Add(new XElement("channels", audioChannelCount));
                        audio.Add(new XElement("codec", MediaInfoFormatter.FormatAudioCodec(movieFile.MediaInfo, sceneName)));
                        audio.Add(new XElement("language", movieFile.MediaInfo.AudioLanguages));
                        streamDetails.Add(audio);

                        if (movieFile.MediaInfo.Subtitles != null && movieFile.MediaInfo.Subtitles.Length > 0)
                        {
                            var subtitle = new XElement("subtitle");
                            subtitle.Add(new XElement("language", movieFile.MediaInfo.Subtitles));
                            streamDetails.Add(subtitle);
                        }

                        fileInfo.Add(streamDetails);
                        details.Add(fileInfo);

                        foreach (var cast in tmdbMovie.Credits.Cast)
                        {
                            if (cast.Name.IsNotNullOrWhiteSpace() && cast.Character.IsNotNullOrWhiteSpace())
                            {
                                var actorElement = new XElement("actor");

                                actorElement.Add(new XElement("name", cast.Name));
                                actorElement.Add(new XElement("role", cast.Character));
                                actorElement.Add(new XElement("order", cast.Order));

                                if (cast.ProfilePath.IsNotNullOrWhiteSpace())
                                {
                                    actorElement.Add(new XElement("thumb", BaseImageUrl + "/original" + cast.ProfilePath));
                                }

                                details.Add(actorElement);
                            }
                        }

                        details.Add(new XElement("dateadded", movieFile.DateAdded.ToString("s")));
                    }

                    doc.Add(details);
                    doc.Save(xw);

                    xmlResult += doc.ToString();
                    xmlResult += Environment.NewLine;
                }
            }

            if (Settings.MovieMetadataURL)
            {
                xmlResult += "https://www.themoviedb.org/movie/" + movie.TmdbId;
                xmlResult += Environment.NewLine;

                xmlResult += "https://www.imdb.com/title/" + movie.ImdbId;
                xmlResult += Environment.NewLine;
            }

            var metadataFileName = GetMovieMetadataFilename(movieFile.RelativePath);

            return string.IsNullOrEmpty(xmlResult) ? null : new MetadataFileResult(metadataFileName, xmlResult.Trim(Environment.NewLine.ToCharArray()));
        }

        public override List<ImageFileResult> MovieImages(Movie movie, MovieFile movieFile)
        {
            if (!Settings.MovieImages)
            {
                return new List<ImageFileResult>();
            }

            return ProcessMovieImages(movie, movieFile).ToList();
        }

        private IEnumerable<ImageFileResult> ProcessMovieImages(Movie movie, MovieFile movieFile)
        {
            _logger.Debug("Generating Movie Images for: {0}", Path.Combine(movie.Path, movieFile.RelativePath));

            if (TmdbClient == null)
            {
                TmdbClient = new TMDbClient(Settings.ApiKey);
            }

            var movieMetadataLanguage = (Settings.MovieMetadataLanguage == (int)Language.Original) ? (int)movie.OriginalLanguage : Settings.MovieMetadataLanguage;
            var selectedSettingsLanguage = Language.FindById(movieMetadataLanguage);
            var isoLanguage = IsoLanguages.Get(selectedSettingsLanguage);

            var tmdbMovie = TmdbClient.GetMovieAsync(movie.TmdbId, isoLanguage.TwoLetterCode).Result;

            if (tmdbMovie != null)
            {
                var baseDestination = new StringBuilder();

                if (!Settings.UseMovieImages)
                {
                    baseDestination.Append(Path.GetFileNameWithoutExtension(movieFile.RelativePath));
                    baseDestination.Append('-');
                }

                if (tmdbMovie.BackdropPath != null)
                {
                    yield return new ImageFileResult(GetMovieImageFilename(movieFile.RelativePath, true), BaseImageUrl + "/original" + tmdbMovie.BackdropPath);
                }

                if (tmdbMovie.PosterPath != null)
                {
                    yield return new ImageFileResult(GetMovieImageFilename(movieFile.RelativePath, false), BaseImageUrl + "/original" + tmdbMovie.PosterPath);
                }
            }
        }

        private string GetMovieMetadataFilename(string movieFilePath)
        {
            if (Settings.UseMovieNfo)
            {
                return Path.Combine(Path.GetDirectoryName(movieFilePath), "movie.nfo");
            }
            else
            {
                return Path.ChangeExtension(movieFilePath, "nfo");
            }
        }

        private string GetMovieImageFilename(string movieFilePath, bool isFanart)
        {
            var baseDestination = new StringBuilder();

            if (!Settings.UseMovieImages)
            {
                baseDestination.Append(Path.GetFileNameWithoutExtension(movieFilePath));
                baseDestination.Append('-');
            }

            if (isFanart)
            {
                baseDestination.Append("fanart.jpg");
            }
            else
            {
                baseDestination.Append("poster.jpg");
            }

            return baseDestination.ToString();
        }

        private string GetYoutubeTrailer(TMDbLibMovies.Movie tmdbMovie)
        {
            var youtubeTrailer = string.Empty;

            if (tmdbMovie?.Videos.Results != null)
            {
                var youTubeId = tmdbMovie.Videos.Results.FirstOrDefault(video => video.Site == "YouTube" && video.Type == "Trailer")?.Key;
                if (youTubeId != null)
                {
                    youtubeTrailer = "https://www.youtube.com/watch?v=" + youTubeId;
                }
            }

            return youtubeTrailer;
        }
    }
}
