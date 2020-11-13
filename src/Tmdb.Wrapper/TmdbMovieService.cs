using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMDbLib.Client;

namespace Tmdb.Wrapper
{
    public interface ITmdbService
    {
        Task GetMovieAsync();
    }

    public class TmdbService : ITmdbService
    {
        private readonly TMDbClient _client = new TMDbClient("b3f5997222c6f8c102df3a24c1ed1213");

        public async Task GetMovieAsync()
        {
            var movie = await _client.GetMovieAsync(47964);

            Console.WriteLine($"Movie name: {movie.Title}");
        }

        public async Task<HashSet<int>> GetChangedMovies(DateTime startTime)
        {
            var changesMovies = await _client.GetChangesMoviesAsync(startDate: startTime);

            return new HashSet<int>(changesMovies.Results.Select(changeMovie => changeMovie.Id));
        }
    }
}
