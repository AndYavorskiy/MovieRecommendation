using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MovieRecommendationApp.BLL.Models;
using MovieRecommendationApp.DAL.Contexts;
using MovieRecommendationApp.DAL.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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

        public async Task<List<MovieModel>> Search(MovieSearchFilter filter)
        {
            return (await dbContext.Movies
                 .OrderByDescending(x => x.ReleaseDate)
                 .Skip(filter.PageIndex * filter.PageSize)
                 .Take(filter.PageSize)
                 .ToListAsync())
                 .Select(x => MapMovieToModel(x, "w400"))
                 .ToList();
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

        public async Task<List<MovieModel>> GetRecommendations(int id)
        {
            int topSimilar = 10;

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
                .Take(topSimilar)
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
            Genres = movie.Genres,
            ImdbId = movie.ImdbId,
            OriginalLanguage = movie.OriginalLanguage,
            OriginalTitle = movie.OriginalTitle,
            Overview = movie.Overview,
            Popularity = movie.Popularity,
            PosterPath = $"http://image.tmdb.org/t/p/{width}{movie.PosterPath}",
            ProductionCompanies = movie.ProductionCompanies,
            ProductionCountries = movie.ProductionCountries,
            ReleaseDate = movie.ReleaseDate,
            Revenue = movie.Revenue,
            Runtime = movie.Runtime,
            SpokenLanguages = movie.SpokenLanguages,
            Status = movie.Status,
            Title = movie.Title,
            VoteAverage = movie.VoteAverage,
            VoteCount = movie.VoteCount,
            Crew = movie.Crew,
            Cast = movie.Cast,
            Keywords = movie.Keywords
        };
    }
}
