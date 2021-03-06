﻿using Accord.Diagnostics;
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
        private readonly IDataParserService dataParserService;
        private readonly IDistributedCache distributedCache;

        public MovieService(
            MovieRecommendationDbContext dbContext,
            IDataParserService dataParserService,
            IDistributedCache distributedCache)
        {
            this.dbContext = dbContext;
            this.dataParserService = dataParserService;
            this.distributedCache = distributedCache;
        }

        public async Task<ListData<MovieModel>> Search(MovieSearchFilterModel filter)
        {
            var query = dbContext.Movies
                .Include(x => x.MoviesGenres)
                .ThenInclude(x => x.Genre)
                .OrderByDescending(x => x.ReleaseDate)
                .Where(x => x.IsPosterAvailable)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.FilterText))
            {
                query = query.Where(x => x.Title.ToUpper().Contains(filter.FilterText.Trim().ToUpper()));
            }

            if (filter.Genres != null && filter.Genres.Any())
            {
                query = query.Where(x => x.MoviesGenres.Any(x => filter.Genres.Contains(x.GenreId)));
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .Skip(filter.PageIndex * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            return new ListData<MovieModel>
            {
                Items = items.Select(x =>
                {

                    try
                    {
                        return MapMovieToModel(x, "w400");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        return null;
                    }

                })

                .Where(x => x != null).ToList(),
                TotalCount = totalCount
            };
        }

        public async Task<MovieModel> Get(int id)
        {
            var movie = await dbContext.Movies
                .Include(x => x.MoviesGenres)
                .ThenInclude(x => x.Genre)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (movie == null)
            {
                throw new Exception("Movie not found.");
            }

            return MapMovieToModel(movie, "original");
        }

        public async Task SetNoPoster(int id)
        {
            var movie = await dbContext.Movies
                .FirstOrDefaultAsync(x => x.Id == id);

            if (movie == null)
            {
                throw new Exception("Movie not found.");
            }

            movie.IsPosterAvailable = false;
            dbContext.Movies.Update(movie);
            await dbContext.SaveChangesAsync();
        }

        public async Task<List<MovieModel>> GetRecommendations(int id, int top)
        {
            var movies = await dbContext.Movies
                .Where(x => x.IsPosterAvailable)
                .OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Title })
                .ToListAsync();

            var movie = movies.FirstOrDefault(x => x.Id == id);

            if (movie == null)
            {
                throw new Exception("Movie not found.");
            }

            int movieIndex = movies.IndexOf(movie);

            var serealizedSimilariityList = await distributedCache.GetStringAsync($"{SimilarityItems}-{movieIndex}");
            if (serealizedSimilariityList == null)
            {
                await dataParserService.SyncSimilarityMatrix();
                serealizedSimilariityList = await distributedCache.GetStringAsync($"{SimilarityItems}-{movieIndex}");
            }

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

            if (ClusteredMovies.Data != null && ClusteredMovies.Data.Any() && ClusteredMovies.Data.ContainsKey(id))
            {
                var claster = ClusteredMovies.Data[id];

                similarMoviesIds = ClusteredMovies.Data
                    .Where(x => x.Value == claster)
                    .Select(x => x.Key)
                    .Where(x => x != id)
                    .ToList();
            }

            var similarMovies = await dbContext.Movies
                .Include(x => x.MoviesGenres)
                .ThenInclude(x => x.Genre)
                .Where(x => similarMoviesIds.Contains(x.Id))
                .ToListAsync();

            Console.WriteLine($"------------------------- Original movie -----------------------------");
            Console.WriteLine($" \t{movie.Id}\t{movie.Title}");
            Console.WriteLine($"-------------------------Recommendations------------------------------");

            int i = 1;
            foreach (var item in similarMovies)
            {
                Console.WriteLine($"{i++}\t{item.Id}\t{item.Title}");
            }

            Console.WriteLine($"----------------------------------------------------------------------");

            return similarMoviesIds
                .Join(similarMovies,
                    x => x,
                    y => y.Id,
                    (x, y) => MapMovieToModel(y, "w400"))
                .ToList();
        }

        public Task<List<GenreModel>> GetGenres()
            => dbContext.Genres
            .OrderBy(x => x.Name)
            .Select(x => new GenreModel
            {
                Id = x.Id,
                Name = x.Name
            })
            .ToListAsync();

        private static MovieModel MapMovieToModel(Movie movie, string width) => new MovieModel
        {
            Id = movie.Id,
            OriginalId = movie.Id,
            Adult = movie.Adult,
            Budget = movie.Budget,
            Genres = movie.MoviesGenres.Select(x => new IdName { id = x.Genre.Id, name = x.Genre.Name }).ToArray(),
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
