using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MovieRecommendationApp.BLL.Models;
using MovieRecommendationApp.BLL.ParseModels;
using MovieRecommendationApp.DAL.Contexts;
using MovieRecommendationApp.DAL.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MovieRecommendationApp.BLL.Services
{
    public class MovieService : IMovieService
    {
        private const string SimilarityItems = "SimilarityItems";

        private readonly MovieRecommendationDbContext dbContext;
        private readonly IDistributedCache distributedCache;

        public MovieService(
            MovieRecommendationDbContext dbContext,
            IDistributedCache distributedCache)
        {
            this.dbContext = dbContext;
            this.distributedCache = distributedCache;
        }

        public async Task<ListData<MovieModel>> Search(MovieSearchFilter filter)
        {
            var query = dbContext.Movies
                .OrderByDescending(x => x.ReleaseDate)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.FilterText))
            {
                query = query.Where(x => x.Title.ToUpper().Contains(filter.FilterText.Trim().ToUpper()));
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .Skip(filter.PageIndex * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            return new ListData<MovieModel>
            {
                Items = items.Select(x => MapMovieToModel(x, "w400")).ToList(),
                TotalCount = totalCount
            };
        }

        public async Task<MovieModel> Get(int id)
        {
            var movie = await dbContext.Movies
                  .FirstOrDefaultAsync(x => x.Id == id);

            if (movie == null)
            {
                throw new Exception("Movie not found.");
            }

            return MapMovieToModel(movie, "original");
        }

        public async Task<List<MovieModel>> GetRecommendations(int id, int top)
        {
            var movies = await dbContext.Movies
                .OrderBy(x => x.Id)
                .Select(x => new { x.Id })
                .ToListAsync();

            var movie = movies.FirstOrDefault(x => x.Id == id);

            if (movie == null)
            {
                throw new Exception("Movie not found.");
            }

            int movieIndex = movies.IndexOf(movie);

            var serealizedSimilariityList = await distributedCache.GetStringAsync($"{SimilarityItems}-{movieIndex}");

            var movieSimilarities = serealizedSimilariityList == null
                ? new List<double>()
                : JsonConvert.DeserializeObject<List<double>>(serealizedSimilariityList);

            var similarMoviesIds = movies.Zip(movieSimilarities,
                (x, similarity) => new { x.Id, similarity })
                .OrderByDescending(x => x.similarity)
                .Select(x => x.Id)
                .Where(x => x != id)                //remove similarity (1) with itself
                .Take(top)
                .ToList();

            var similarMovies = await dbContext.Movies
                .Where(x => similarMoviesIds.Contains(x.Id))
                .ToListAsync();

            return similarMoviesIds
                .Join(similarMovies,
                    x => x,
                    y => y.Id,
                    (x, y) => MapMovieToModel(y, "w400"))
                .ToList();
        }

        private static MovieModel MapMovieToModel(Movie movie, string width) => new MovieModel
        {
            Id = movie.Id,
            OriginalId = movie.Id,
            Adult = movie.Adult,
            Budget = movie.Budget,
            Genres = JsonConvert.DeserializeObject<IdName[]>(movie.Genres),
            ImdbId = movie.ImdbId,
            OriginalLanguage = movie.OriginalLanguage,
            OriginalTitle = movie.OriginalTitle,
            Overview = movie.Overview,
            Popularity = movie.Popularity,
            PosterPath = $"http://image.tmdb.org/t/p/{width}{movie.PosterPath}",
            Companies = JsonConvert.DeserializeObject<IdName[]>(movie.ProductionCompanies),
            Countries = JsonConvert.DeserializeObject<CountryIsoName[]>(movie.ProductionCountries)
                .Select(x => new IsoName() { Iso = x.iso_3166_1, Name = x.name })
                .ToArray(),
            ReleaseDate = movie.ReleaseDate,
            Revenue = movie.Revenue,
            Runtime = movie.Runtime,
            Languages = JsonConvert.DeserializeObject<LanguageIsoName[]>(movie.SpokenLanguages)
                .Select(x => new IsoName() { Iso = x.iso_639_1, Name = x.name })
                .ToArray(),
            Status = movie.Status,
            Title = movie.Title,
            VoteAverage = movie.VoteAverage,
            VoteCount = movie.VoteCount,
            Directors = JsonConvert.DeserializeObject<CrewModel[]>(movie.Crew)
                .Where(x => string.Equals(x.job, "Director", StringComparison.InvariantCultureIgnoreCase))
                .Take(3)
                .Select(x => x.name)
                .ToArray(),
            Cast = JsonConvert.DeserializeObject<CastBigModel[]>(movie.Cast)
                .OrderBy(x => x.order)
                .Take(5)
                .Select(x => new CharacterActorModel()
                {
                    Character = x.character,
                    ActorName = x.name
                })
                .ToArray(),
            Keywords = JsonConvert.DeserializeObject<IdName[]>(movie.Keywords)
        };
    }
}
