using Accord.MachineLearning;
using Accord.Math.Distances;
using CsvHelper;
using EFCore.BulkExtensions;
using Iveonik.Stemmers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MovieRecommendationApp.BLL.ParseModels;
using MovieRecommendationApp.DAL.Contexts;
using MovieRecommendationApp.DAL.Entities;
using Newtonsoft.Json;
using StopWord;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace MovieRecommendationApp.BLL.Services
{
    public class DataParserService : IDataParserService
    {
        private const string SimilarityItems = "SimilarityItems";
        private const string MatrixesFolder = "DataMatrixes";
        private const string CreditsGenresKeywordsCastSimilarityMatrix = "credits_genres_keywords_cast_similarity_matrix.csv";

        private readonly MovieRecommendationDbContext dbContext;
        private readonly IDistributedCache distributedCache;

        public DataParserService(MovieRecommendationDbContext dbContext,
            IDistributedCache distributedCache)
        {
            this.dbContext = dbContext;
            this.distributedCache = distributedCache;
        }

        public async Task ParseData()
        {
            var movies = GetMovies();
            var credits = GetCredits();
            var keywords = GetKeywords();

            var parsedMovies = movies
                .Where(x => x.vote_average.HasValue
                        && x.vote_count.HasValue
                        && x.popularity.HasValue
                        && !x.video)
                .Where(x => x.status == "Released")
                .Where(x => x.popularity > 1)
                .Where(x => x.release_date.HasValue)
                .ToList();

            await dbContext.MoviesGenres.BatchDeleteAsync();
            await dbContext.Movies.BatchDeleteAsync();
            await dbContext.Genres.BatchDeleteAsync();

            var genres = parsedMovies.Select(x => JsonConvert.DeserializeObject<IdName[]>(x.genres))
                .SelectMany(x => x.Select(y => y.name))
                .Distinct()
                .OrderBy(x => x)
                .Select(x => new Genre { Name = x })
                .ToList();

            dbContext.Genres.AddRange(genres);
            await dbContext.SaveChangesAsync();

            var moviesToSave = parsedMovies
                .Join(credits,
                x => x.id,
                x => x.id,
                (movie, credit) => new Movie
                {
                    OriginalId = movie.id,
                    Adult = movie.adult,
                    Budget = movie.budget,
                    Genres = movie.genres,
                    ImdbId = movie.imdb_id,
                    OriginalLanguage = movie.original_language,
                    OriginalTitle = movie.original_title,
                    Overview = movie.overview,
                    Popularity = movie.popularity.Value,
                    PosterPath = movie.poster_path,
                    ProductionCompanies = movie.production_companies,
                    ProductionCountries = movie.production_countries,
                    ReleaseDate = movie.release_date,
                    Revenue = movie.revenue,
                    Runtime = movie.runtime,
                    SpokenLanguages = movie.spoken_languages,
                    Status = movie.status,
                    Title = movie.title,
                    VoteAverage = movie.vote_average.Value,
                    VoteCount = movie.vote_count.Value,
                    Crew = credit.crew,
                    Cast = credit.cast,
                    Keywords = keywords.FirstOrDefault(x => x.id == movie.id)?.keywords
                })
                .OrderByDescending(x => x.ReleaseDate)
                .Take(5000)
                .ToList();

            await dbContext.BulkInsertAsync(moviesToSave);

            var moviesFromDb = await dbContext.Movies.Select(x => new { x.Id, x.Genres }).ToListAsync();
            var genresFromDb = genres.ToDictionary(x => x.Name, x => x.Id);

            var moviesGenres = moviesFromDb.SelectMany(x => JsonConvert.DeserializeObject<IdName[]>(x.Genres)
                .Select(g => new MovieGenre
                {
                    MovieId = x.Id,
                    GenreId = genresFromDb[g.name]
                })
                .ToList());

            dbContext.MoviesGenres.AddRange(moviesGenres);
            await dbContext.SaveChangesAsync();
        }

        public async Task GenerateCreditsGenresKeywordsCastSimilarityMatrix()
        {
            var words = await GetTorenizedMoviesWords();

            var codebook = new TFIDF()
            {
                Tf = TermFrequency.Log,
                Idf = InverseDocumentFrequency.Default
            };

            codebook.Learn(words);

            var bow = codebook.Transform(words);

            Console.WriteLine($"bows: {bow.Length}, words: {bow[0].Length}");

            var start = DateTime.Now;

            var maxIterations = bow.Length * bow.Length;
            var currentIteration = 0;

            var folderPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), MatrixesFolder);
            var filePath = Path.Combine(folderPath, CreditsGenresKeywordsCastSimilarityMatrix);

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            using (var sw = new StreamWriter(filePath))
            using (var csvWriter = new CsvWriter(sw, CultureInfo.InvariantCulture))
            {
                for (int i = 0; i < bow.Length; i++)
                {
                    for (int j = 0; j < bow.Length; j++)
                    {
                        csvWriter.WriteField(Math.Round(new PearsonCorrelation().Similarity(bow[i], bow[j]), 3));
                        currentIteration++;
                    }

                    if (i == 0)
                    {
                        Console.WriteLine($"Estimated time ~ {(DateTime.Now - start).TotalSeconds * bow.Length / 60} min.");
                    }

                    Console.WriteLine($"{i}. {(float)currentIteration / maxIterations * 100}");
                    csvWriter.NextRecord();
                }
            }

            Console.WriteLine($"{DateTime.Now - start}");

            await UploanSimilarityMatrixToCache(filePath);
        }

        public async Task SyncSimilarityMatrix()
        {
            var folderPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), MatrixesFolder);
            var filePath = Path.Combine(folderPath, CreditsGenresKeywordsCastSimilarityMatrix);

            await UploanSimilarityMatrixToCache(filePath);
        }

        private async Task<string[][]> GetTorenizedMoviesWords()
        {
            var movies = await dbContext.Movies
              .OrderBy(x => x.Id)
              .Select(x => new MovieProcessingModel
              {
                  Id = x.Id,
                  Crew = x.Crew,
                  Cast = x.Cast,
                  Keywords = x.Keywords,
                  Genres = x.Genres,
                  Overview = x.Overview
              })
              .ToListAsync();

            var stemmer = new EnglishStemmer();

            return movies
                .Select(x => GetMovieData(x, stemmer))
                .ToArray()
                .Tokenize();
        }

        private static string GetMovieData(MovieProcessingModel movie, EnglishStemmer stemmer)
        {
            var top = 3;

            var crew = JsonConvert.DeserializeObject<CrewModel[]>(movie.Crew)
                .Where(x => string.Equals(x.job, "Director", StringComparison.InvariantCultureIgnoreCase))
                .Take(top)
                .Select(x => $"Crew-{x.name.Replace(" ", "")}")
                .ToList();

            var cast = JsonConvert.DeserializeObject<CastBigModel[]>(movie.Cast)
                .OrderBy(x => x.order)
                .Take(top)
                .Select(x => $"Cast-{x.name.Replace(" ", "")}")
                .ToList();

            var keywords = JsonConvert.DeserializeObject<IdName[]>(movie.Keywords)
                .Take(top)
                .Select(x => $"Kw-{x.name.Replace(" ", "")}")
                .ToList();

            var genres = JsonConvert.DeserializeObject<IdName[]>(movie.Genres)
                .Take(top)
                .Select(x => $"Gen-{x.name.Replace(" ", "")}")
                .ToList();

            var overviewWords = movie.Overview
                .RemoveStopWords("en")
                .Tokenize()
                .Select(stemmer.Stem)
                .ToList();

            return JoinWords(new string[] {
                JoinWords(crew),
                JoinWords(cast),
                JoinWords(keywords),
                JoinWords(genres),
                JoinWords(overviewWords),
            });
        }

        private static string JoinWords(IEnumerable<string> words) => string.Join(" ", words);

        private static List<MovieModelBig> GetMovies()
        {
            var movies = new List<MovieModelBig>();

            using (var reader = new StreamReader(GetDataSourcesPath("movies_metadata.csv")))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Configuration.Delimiter = ",";
                csv.Configuration.HasHeaderRecord = true;

                movies = csv.GetRecords<MovieModelBig>()
                    .GroupBy(x => x.id)
                    .Select(x => x.First())
                    .Select(x =>
                    {
                        x.genres = x.genres.Replace(@"\xa", " ").Replace(@"\\x92", "'").Replace(@"\x", " ").Replace(": None", ": 'None'");

                        return x;
                    })
                    .ToList();
            }

            return movies
                .OrderBy(x => x.id)
                .ToList();
        }

        private static List<CreditsModel> GetCredits()
        {
            var credits = new List<CreditsModel>();

            using (var reader = new StreamReader(GetDataSourcesPath("credits.csv")))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Configuration.Delimiter = ",";
                csv.Configuration.HasHeaderRecord = true;

                credits = csv.GetRecords<CreditsModel>()
                    .GroupBy(x => x.id)
                    .Select(x => x.First())
                    .Select(x =>
                    {
                        x.crew = x.crew.Replace(@"\xa", " ").Replace(@"\\x92", "'").Replace(@"\x", " ").Replace(": None", ": 'None'");
                        x.cast = x.cast.Replace(@"\xa", " ").Replace(@"\\x92", "'").Replace(@"\x", " ").Replace(": None", ": 'None'");

                        return x;
                    })
                    .ToList();
            }

            return credits;
        }

        private static List<KeywordsModel> GetKeywords()
        {
            var keywords = new List<KeywordsModel>();

            using (var reader = new StreamReader(GetDataSourcesPath("keywords.csv")))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Configuration.Delimiter = ",";
                csv.Configuration.HasHeaderRecord = true;

                keywords = csv.GetRecords<KeywordsModel>()
                    .GroupBy(x => x.id)
                    .Select(x => x.First())
                    .Select(x =>
                    {
                        x.keywords = x.keywords.Replace(@"\xa", " ");

                        return x;
                    })
                    .ToList();
            }

            return keywords;
        }

        private static string GetDataSourcesPath(string fileName) => GetPath("DataSources", fileName);

        private static string GetPath(string folder, string fileName)
           => Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), folder, fileName);

        private async Task UploanSimilarityMatrixToCache(string path)
        {
            //Remove old cache
            for (int i = 0; i < 100000; i++)
            {
                await distributedCache.RemoveAsync($"{SimilarityItems}-{i}");
            }

            //Save new cache
            var similariityMatrix = ReadSimilarityMatrix(path);

            int index = 0;
            foreach (var row in similariityMatrix)
            {
                var serealizedRow = JsonConvert.SerializeObject(row);
                await distributedCache.SetStringAsync($"{SimilarityItems}-{index}", serealizedRow);
                index++;
            }
        }

        private static List<List<double>> ReadSimilarityMatrix(string path)
        {
            var data = new List<List<double>>();

            using (var sw = new StreamReader(path))
            using (var csvRearer = new CsvParser(sw, CultureInfo.InvariantCulture))
            {
                var row = csvRearer.Read();

                while (row != null)
                {
                    var rowData = row.Select(x => double.Parse(x.Replace(".", ","))).ToList();
                    data.Add(rowData);

                    row = csvRearer.Read();
                }
            }

            return data;
        }

        private class MovieProcessingModel
        {
            public int Id { get; set; }
            public string Crew { get; set; }
            public string Cast { get; set; }
            public string Keywords { get; set; }
            public string Genres { get; set; }
            public string Overview { get; set; }
        }
    }
}
