using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.Parser;
using TMDbLib.Client;
using TMDbLibMovies = TMDbLib.Objects.Movies;

namespace NzbDrone.Core.MetadataSource.TMDb
{
    public class TMDbProxy : IProvideMovieInfo, ISearchForNewMovie
    {
        private readonly IConfigService _configService;
        private readonly IMovieService _movieService;
        private readonly IMovieTranslationService _movieTranslationService;
        private readonly Logger _logger;

        private readonly TMDbClient _client = new TMDbClient("b3f5997222c6f8c102df3a24c1ed1213");
        private readonly TMDbLibMovies.MovieMethods _movieMethods =
            TMDbLibMovies.MovieMethods.AlternativeTitles |
            TMDbLibMovies.MovieMethods.Credits |
            TMDbLibMovies.MovieMethods.Images |
            TMDbLibMovies.MovieMethods.Keywords |
            TMDbLibMovies.MovieMethods.Releases |
            TMDbLibMovies.MovieMethods.Videos |
            TMDbLibMovies.MovieMethods.Translations |
            TMDbLibMovies.MovieMethods.Similar |
            TMDbLibMovies.MovieMethods.Reviews |
            TMDbLibMovies.MovieMethods.Lists |
            TMDbLibMovies.MovieMethods.Changes |

            //TMDbLibMovies.MovieMethods.AccountStates |
            TMDbLibMovies.MovieMethods.ReleaseDates |
            TMDbLibMovies.MovieMethods.Recommendations |
            TMDbLibMovies.MovieMethods.ExternalIds;

        public TMDbProxy(
            IConfigService configService,
            IMovieService movieService,
            IMovieTranslationService movieTranslationService,
            Logger logger)
        {
            _configService = configService;
            _movieService = movieService;
            _movieTranslationService = movieTranslationService;
            _logger = logger;
        }

        public HashSet<int> GetChangedMovies(DateTime startTime)
        {
            // Round down to the hour to ensure we cover gap and don't kill cache every call
            var cacheAdjustedStart = startTime.AddMinutes(-15);
            var startDate = cacheAdjustedStart.Date.AddHours(cacheAdjustedStart.Hour);

            var changesMovies = _client.GetChangesMoviesAsync(startDate: startDate).Result;

            return new HashSet<int>(changesMovies.Results.Select(changeMovie => changeMovie.Id));
        }

        public Tuple<Movie, List<Credit>> GetMovieInfo(int tmdbId)
        {
            var tmdbMovie = _client.GetMovieAsync(tmdbId, "es-ES", _movieMethods).Result;

            var resourceMovie = MapTMDbMovie(tmdbMovie);
            var movie = MapMovie(resourceMovie);

            var credits = new List<Credit>();
            credits.AddRange(resourceMovie.Credits.Cast.Select(MapCast));
            credits.AddRange(resourceMovie.Credits.Crew.Select(MapCrew));

            return new Tuple<Movie, List<Credit>>(movie, credits.ToList());
        }

        public List<Movie> GetBulkMovieInfo(List<int> tmdbIds)
        {
            var movies = new List<Movie>();
            foreach (var tmdbId in tmdbIds)
            {
                var tmdbMovie = _client.GetMovieAsync(tmdbId, "es-ES", _movieMethods).Result;

                var resourceMovie = MapTMDbMovie(tmdbMovie);
                movies.Add(MapMovie(resourceMovie));
            }

            return movies;
        }

        public Movie GetMovieByImdbId(string imdbId)
        {
            var tmdbMovie = _client.GetMovieAsync(imdbId).Result;

            var resourceMovie = MapTMDbMovie(tmdbMovie);
            var movie = MapMovie(resourceMovie);

            return movie;
        }

        public Movie MapMovie(MovieResource resource)
        {
            var movie = new Movie();
            var altTitles = new List<AlternativeTitle>();

            movie.TmdbId = resource.TmdbId;
            movie.ImdbId = resource.ImdbId;
            movie.Title = resource.Title;
            movie.OriginalTitle = resource.OriginalTitle;
            movie.TitleSlug = resource.TitleSlug;
            movie.CleanTitle = resource.Title.CleanMovieTitle();
            movie.SortTitle = Parser.Parser.NormalizeTitle(resource.Title);
            movie.Overview = resource.Overview;

            movie.AlternativeTitles.AddRange(resource.AlternativeTitles.Select(MapAlternativeTitle));

            movie.Translations.AddRange(resource.Translations.Select(MapTranslation));

            movie.OriginalLanguage = IsoLanguages.Find(resource.OriginalLanguage.ToLower())?.Language ?? Language.English;

            movie.Website = resource.Homepage;
            movie.InCinemas = resource.InCinema;
            movie.PhysicalRelease = resource.PhysicalRelease;
            movie.DigitalRelease = resource.DigitalRelease;

            movie.Year = resource.Year;

            //If the premier differs from the TMDB year, use it as a secondary year.
            if (resource.Premier.HasValue && resource.Premier?.Year != movie.Year)
            {
                movie.SecondaryYear = resource.Premier?.Year;
            }

            movie.Images = resource.Images.SelectList(MapImage);

            if (resource.Runtime != null)
            {
                movie.Runtime = resource.Runtime.Value;
            }

            var certificationCountry = _configService.CertificationCountry.ToString();

            movie.Certification = resource.Certifications.FirstOrDefault(m => m.Country == certificationCountry)?.Certification;
            movie.Ratings = resource.Ratings.Select(MapRatings).FirstOrDefault() ?? new Ratings();
            movie.Genres = resource.Genres;
            movie.Recommendations = resource.Recommendations?.Select(r => r.TmdbId).ToList() ?? new List<int>();

            var now = DateTime.Now;

            movie.Status = MovieStatusType.Announced;

            if (resource.InCinema.HasValue && now > resource.InCinema)
            {
                movie.Status = MovieStatusType.InCinemas;

                if (!resource.PhysicalRelease.HasValue && !resource.DigitalRelease.HasValue && now > resource.InCinema.Value.AddDays(90))
                {
                    movie.Status = MovieStatusType.Released;
                }
            }

            if (resource.PhysicalRelease.HasValue && now >= resource.PhysicalRelease)
            {
                movie.Status = MovieStatusType.Released;
            }

            if (resource.DigitalRelease.HasValue && now >= resource.DigitalRelease)
            {
                movie.Status = MovieStatusType.Released;
            }

            movie.YouTubeTrailerId = resource.YoutubeTrailerId;
            movie.Studio = resource.Studio;

            if (resource.Collection != null)
            {
                movie.Collection = new MovieCollection { Name = resource.Collection.Name, TmdbId = resource.Collection.TmdbId };
            }

            return movie;
        }

        public Movie MapMovieToTmdbMovie(Movie movie)
        {
            try
            {
                var newMovie = movie;

                if (movie.TmdbId > 0)
                {
                    newMovie = _movieService.FindByTmdbId(movie.TmdbId);

                    if (newMovie == null)
                    {
                        newMovie = GetMovieInfo(movie.TmdbId).Item1;
                    }
                }
                else if (movie.ImdbId.IsNotNullOrWhiteSpace())
                {
                    newMovie = GetMovieByImdbId(movie.ImdbId);
                }
                else
                {
                    var yearStr = "";
                    if (movie.Year > 1900)
                    {
                        yearStr = $" {movie.Year}";
                    }

                    newMovie = SearchForNewMovie(movie.Title + yearStr).FirstOrDefault();
                }

                if (newMovie == null)
                {
                    _logger.Warn("Couldn't map movie {0} to a movie on The Movie DB. It will not be added :(", movie.Title);
                    return null;
                }

                newMovie.Path = movie.Path;
                newMovie.RootFolderPath = movie.RootFolderPath;
                newMovie.ProfileId = movie.ProfileId;
                newMovie.Monitored = movie.Monitored;
                newMovie.MovieFile = movie.MovieFile;
                newMovie.MinimumAvailability = movie.MinimumAvailability;
                newMovie.Tags = movie.Tags;

                return newMovie;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Couldn't map movie {0} to a movie on The Movie DB. It will not be added :(", movie.Title);
                return null;
            }
        }

        public List<Movie> SearchForNewMovie(string title)
        {
            try
            {
                var lowerTitle = title.ToLower();

                lowerTitle = lowerTitle.Replace(".", "");

                var parserTitle = lowerTitle;

                var parserResult = Parser.Parser.ParseMovieTitle(title, true);

                int? yearTerm = null;

                if (parserResult != null && parserResult.MovieTitle != title)
                {
                    //Parser found something interesting!
                    parserTitle = parserResult.MovieTitle.ToLower().Replace(".", " "); //TODO Update so not every period gets replaced (e.g. R.I.P.D.)
                    if (parserResult.Year > 1800)
                    {
                        yearTerm = parserResult.Year;
                    }

                    if (parserResult.ImdbId.IsNotNullOrWhiteSpace())
                    {
                        try
                        {
                            var movieLookup = GetMovieByImdbId(parserResult.ImdbId);
                            return movieLookup == null ? new List<Movie>() : new List<Movie> { _movieService.FindByTmdbId(movieLookup.TmdbId) ?? movieLookup };
                        }
                        catch (Exception)
                        {
                            return new List<Movie>();
                        }
                    }
                }

                parserTitle = StripTrailingTheFromTitle(parserTitle);

                if (lowerTitle.StartsWith("imdb:") || lowerTitle.StartsWith("imdbid:"))
                {
                    var slug = lowerTitle.Split(':')[1].Trim();

                    string imdbid = slug;

                    if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace))
                    {
                        return new List<Movie>();
                    }

                    try
                    {
                        var movieLookup = GetMovieByImdbId(imdbid);
                        return movieLookup == null ? new List<Movie>() : new List<Movie> { _movieService.FindByTmdbId(movieLookup.TmdbId) ?? movieLookup };
                    }
                    catch (MovieNotFoundException)
                    {
                        return new List<Movie>();
                    }
                }

                if (lowerTitle.StartsWith("tmdb:") || lowerTitle.StartsWith("tmdbid:"))
                {
                    var slug = lowerTitle.Split(':')[1].Trim();

                    int tmdbid = -1;

                    if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace) || !int.TryParse(slug, out tmdbid))
                    {
                        return new List<Movie>();
                    }

                    try
                    {
                        var movieLookup = GetMovieInfo(tmdbid).Item1;
                        return movieLookup == null ? new List<Movie>() : new List<Movie> { _movieService.FindByTmdbId(movieLookup.TmdbId) ?? movieLookup };
                    }
                    catch (MovieNotFoundException)
                    {
                        return new List<Movie>();
                    }
                }

                var searchTerm = parserTitle.Replace("_", "+").Replace(" ", "+").Replace(".", "+");

                var firstChar = searchTerm.First();

                var tmdbMovies = _client.SearchMovieAsync(searchTerm, year: yearTerm ?? 0).Result;
                var tmdbMoviesIds = tmdbMovies.Results.SelectList(tmdbMovie => tmdbMovie.Id);
                return GetBulkMovieInfo(tmdbMoviesIds);
            }
            catch (HttpException)
            {
                throw new SkyHookException("Search for '{0}' failed. Unable to communicate with TMDb.", title);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, ex.Message);
                throw new SkyHookException("Search for '{0}' failed. Invalid response received from TMDb.", title);
            }
        }

        private MovieResource MapTMDbMovie(TMDbLibMovies.Movie tmdbMovie)
        {
            var releaseDates = tmdbMovie.ReleaseDates.Results;

            var movieResource = new MovieResource
            {
                TmdbId = tmdbMovie.Id,
                ImdbId = tmdbMovie.ImdbId,
                Overview = tmdbMovie.Overview,
                Title = tmdbMovie.Title,
                OriginalTitle = tmdbMovie.OriginalTitle,
                TitleSlug = tmdbMovie.Id.ToString(),
                Ratings = new List<RatingResource>
                {
                    new RatingResource
                    {
                        Count = tmdbMovie.VoteCount,
                        Value = Convert.ToDecimal(tmdbMovie.VoteAverage),
                        Origin = "Tmdb",
                        Type = "User"
                    }
                },
                Runtime = tmdbMovie.Runtime,
                Images = new List<ImageResource>
                {
                    new ImageResource { Url = tmdbMovie.BackdropPath, CoverType = "fanart" },
                    new ImageResource { Url = tmdbMovie.PosterPath, CoverType = "poster" }
                },
                Genres = tmdbMovie.Genres.Select(genre => genre.Name).ToList(),
                Year = tmdbMovie.ReleaseDate?.Year ?? 0,

                //Premier = releaseDates.Where(r => r.ReleaseDates.FirstOrDefault(rd => rd.Type == TMDbLibMovies.ReleaseDateType.Premiere))?.ReleaseDate,
                //InCinema = ,
                //PhysicalRelease = ,
                //DigitalRelease = ,
                AlternativeTitles = tmdbMovie.AlternativeTitles != null ? tmdbMovie.AlternativeTitles.Titles.SelectList(title => new AlternativeTitleResource { Title = title.Title, Language = title.Iso_3166_1, Type = "Tmdb" }) : new List<AlternativeTitleResource>(),

                // TODO: Client doesn't have Translations.Overviews / Translations.Images
                Translations = new List<TranslationResource>(),

                Credits = new Credits { Cast = new List<CastResource>(), Crew = new List<CrewResource>() },
                Studio = tmdbMovie.ProductionCompanies.FirstOrDefault()?.Name,
                YoutubeTrailerId = tmdbMovie.Videos.Results.FirstOrDefault(video => video.Type == "Trailer" && video.Site == "YouTube")?.Key,
                Certifications = new List<CertificationResource>(),
                Status = tmdbMovie.Status,

                // TODO: Collection Images
                Collection = tmdbMovie.BelongsToCollection != null ? new CollectionResource { TmdbId = tmdbMovie.BelongsToCollection.Id, Name = tmdbMovie.BelongsToCollection.Name, Images = null } : null,
                OriginalLanguage = tmdbMovie.OriginalLanguage,
                Homepage = tmdbMovie.Homepage,
                Recommendations = tmdbMovie.Recommendations.Results.SelectList(recomendation => new RecommendationResource { Name = recomendation.Title, TmdbId = recomendation.Id })
            };

            return movieResource;
        }

        private static Credit MapCast(CastResource arg)
        {
            var newActor = new Credit
            {
                Name = arg.Name,
                Character = arg.Character,
                Order = arg.Order,
                CreditTmdbId = arg.CreditId,
                PersonTmdbId = arg.TmdbId,
                Type = CreditType.Cast,
                Images = arg.Images.Select(MapImage).ToList()
            };

            return newActor;
        }

        private static Credit MapCrew(CrewResource arg)
        {
            var newActor = new Credit
            {
                Name = arg.Name,
                Department = arg.Department,
                Job = arg.Job,
                CreditTmdbId = arg.CreditId,
                PersonTmdbId = arg.TmdbId,
                Type = CreditType.Crew,
                Images = arg.Images.Select(MapImage).ToList()
            };

            return newActor;
        }

        private static AlternativeTitle MapAlternativeTitle(AlternativeTitleResource arg)
        {
            var newAlternativeTitle = new AlternativeTitle
            {
                Title = arg.Title,
                SourceType = SourceType.TMDB,
                CleanTitle = arg.Title.CleanMovieTitle(),
                Language = IsoLanguages.Find(arg.Language.ToLower())?.Language ?? Language.English
            };

            return newAlternativeTitle;
        }

        private static MovieTranslation MapTranslation(TranslationResource arg)
        {
            var newAlternativeTitle = new MovieTranslation
            {
                Title = arg.Title,
                Overview = arg.Overview,
                CleanTitle = arg.Title.CleanMovieTitle(),
                Language = IsoLanguages.Find(arg.Language.ToLower())?.Language
            };

            return newAlternativeTitle;
        }

        private static Ratings MapRatings(RatingResource rating)
        {
            if (rating == null)
            {
                return new Ratings();
            }

            return new Ratings
            {
                Votes = rating.Count,
                Value = rating.Value
            };
        }

        private static MediaCover.MediaCover MapImage(ImageResource arg)
        {
            return new MediaCover.MediaCover
            {
                Url = "https://image.tmdb.org/t/p/original" + arg.Url,
                CoverType = MapCoverType(arg.CoverType)
            };
        }

        private static MediaCoverTypes MapCoverType(string coverType)
        {
            switch (coverType.ToLower())
            {
                case "poster":
                    return MediaCoverTypes.Poster;
                case "headshot":
                    return MediaCoverTypes.Headshot;
                case "fanart":
                    return MediaCoverTypes.Fanart;
                default:
                    return MediaCoverTypes.Unknown;
            }
        }

        private Movie MapSearchResult(MovieResource result)
        {
            var movie = _movieService.FindByTmdbId(result.TmdbId);

            if (movie == null)
            {
                movie = MapMovie(result);
            }
            else
            {
                movie.Translations = _movieTranslationService.GetAllTranslationsForMovie(movie.Id);
            }

            return movie;
        }

        private string StripTrailingTheFromTitle(string title)
        {
            if (title.EndsWith(",the"))
            {
                title = title.Substring(0, title.Length - 4);
            }
            else if (title.EndsWith(", the"))
            {
                title = title.Substring(0, title.Length - 5);
            }

            return title;
        }
    }
}
