using Accord.MachineLearning;
using Accord.MachineLearning.Bayes;
using Accord.MachineLearning.DecisionTrees.Learning;
using Accord.MachineLearning.Performance;
using Accord.MachineLearning.VectorMachines;
using Accord.MachineLearning.VectorMachines.Learning;
using Accord.Math;
using Accord.Math.Distances;
using Accord.Math.Optimization.Losses;
using Accord.Statistics.Analysis;
using Accord.Statistics.Distributions.DensityKernels;
using Accord.Statistics.Kernels;
using Accord.Statistics.Models.Markov;
using Accord.Statistics.Models.Markov.Learning;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
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
using System.Security.Cryptography.X509Certificates;
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

        public object BagOfVisualWords { get; private set; }

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
            var movies = await GetMoviesProjection();

            var words = GetTorenizedMoviesWords(movies);

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

        private double[][] GetDataFully(List<MovieProcessingModel> movies)
        {
            var stemmer = new EnglishStemmer();

            var data = movies.Select(x => GetMovieStructuredData(x, stemmer)).ToList();

            var data2 = GetObservationsFull(data
                .Select(x => new List<List<string>>
                {
                    x.Cast,
                    x.Crew,
                    x.Genres,
                    x.Keywords,
                    x.OverviewWords
                }.SelectMany(x => x).ToArray()).ToArray(),
                true);

            return data2;
        }

        private double[][] GetObservationsFull(string[][] words, bool duplicatedOnly = false)
        {
            var codebook = new TFIDF()
            {
                Tf = TermFrequency.Log,
                Idf = InverseDocumentFrequency.Default
            };

            if (duplicatedOnly)
            {
                var tmp = words.SelectMany(x => x).GroupBy(x => x).Select(x => new { Value = x.Key, Count = x.Count() }).OrderByDescending(x => x.Count).ToList();

                var max = tmp.Max(x => x.Count);
                var min = tmp.Min(x => x.Count);

                var limit = min + ((max - min) * 0.50);

                var tmp2 = tmp.Where(x => x.Count >= limit).Select(x => x.Value).ToList();

                words = words.Select(x => x.Where(y => tmp2.Contains(y)).ToArray()).ToArray();
            }

            codebook.Learn(words);

            return codebook.Transform(words);
        }

        public async Task LoadClastersFromFile()
        {
            var movies = (await GetMoviesProjection()).ToList();

            using (var reader = new StreamReader("K-Mean-44103,86675328704.csv"))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Configuration.Delimiter = ",";
                csv.Configuration.HasHeaderRecord = true;

                var data = csv.GetRecords<MovieClaster>();

                ClusteredMovies.Data = data
                  .ToDictionary(x => x.MovieId, y => y.Claster);
            }
        }

        public async Task ComputeClasters()
        {
            var movies = (await GetMoviesProjection()).ToList();

            var observations = GetDataFully(movies);

            //ProcessOptimalKWithElbowMethod(observations, movies);

            try
            {
                // Use a fixed seed for reproducibility
                Accord.Math.Random.Generator.Seed = 0;

                var kmeans = new MiniBatchKMeans(21, Math.Min(movies.Count(), 300));

                var clasters = kmeans.Learn(observations);

                int[] res = clasters.Decide(observations);

                using (var sw = new StreamWriter($"K-Mean-{DateTime.Now.ToOADate()}.csv"))
                using (var csvWriter = new CsvWriter(sw, CultureInfo.InvariantCulture))
                {
                    for (int i = 0; i < res.Length; i++)
                    {
                        csvWriter.WriteField(movies[i].Id);
                        csvWriter.WriteField(res[i]);

                        csvWriter.NextRecord();
                    }
                }

                ClusteredMovies.Data = movies.Zip(res, (movie, claster) => new { movie.Id, claster })
                    .ToDictionary(x => x.Id, y => y.claster);

            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public void ProcessOptimalKWithElbowMethod(double[][] observations, List<MovieProcessingModel> movies)
        {
            for (int k = 5; k <= 35; k++)
            {
                // Use a fixed seed for reproducibility
                Accord.Math.Random.Generator.Seed = 0;

                var kmeans = new MiniBatchKMeans(k, Math.Min(movies.Count(), 300));

                var clasters = kmeans.Learn(observations);

                int[] res = clasters.Decide(observations);

                Console.WriteLine($"{k}\t{kmeans.Error}");
            }
        }

        public async Task SyncSimilarityMatrix()
        {
            var folderPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), MatrixesFolder);
            var filePath = Path.Combine(folderPath, CreditsGenresKeywordsCastSimilarityMatrix);

            await UploanSimilarityMatrixToCache(filePath);
        }

        private string[][] GetTorenizedMoviesWords(List<MovieProcessingModel> movies, bool includeOverview = true)
        {
            var stemmer = new EnglishStemmer();

            return movies
                .Select(x => GetMovieData(x, stemmer, includeOverview))
                .ToArray()
                .Tokenize();
        }

        private async Task<List<MovieProcessingModel>> GetMoviesProjection()
        {
            return await dbContext.Movies
              .Where(x => x.IsPosterAvailable)
              .OrderBy(x => x.Id)
              .Take(300) //---------------------------- Remove ----------------------------
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
        }

        private static StructuredDataModel GetMovieStructuredData(MovieProcessingModel movie, EnglishStemmer stemmer)
        {
            var top = 3;

            var crew = JsonConvert.DeserializeObject<CrewModel[]>(movie.Crew)
                .Where(x => string.Equals(x.job, "Director", StringComparison.InvariantCultureIgnoreCase))
                .Take(top)
                .Select(x => $"Crew{x.name.Replace(" ", "")}".ToLower())
                .ToList();

            var cast = JsonConvert.DeserializeObject<CastBigModel[]>(movie.Cast)
                .OrderBy(x => x.order)
                .Take(top)
                .Select(x => $"Cast{x.name.Replace(" ", "")}".ToLower())
                .ToList();

            var keywords = JsonConvert.DeserializeObject<IdName[]>(movie.Keywords)
                .Take(top)
                .Select(x => $"Kw{x.name.Replace(" ", "")}".ToLower())
                .ToList();

            var genres = JsonConvert.DeserializeObject<IdName[]>(movie.Genres)
                .Take(top)
                .Select(x => $"Gen{x.name.Replace(" ", "")}".ToLower())
                .ToList();

            var overviewWords = movie.Overview
                .RemoveStopWords("en")
                .Tokenize()
                .Select(stemmer.Stem)
                .Select(x => x.ToLower())
                .ToList();

            return new StructuredDataModel
            {
                Crew = crew,
                Cast = cast,
                Keywords = keywords,
                Genres = genres,
                OverviewWords = overviewWords
            };
        }

        private static string GetMovieData(MovieProcessingModel movie, EnglishStemmer stemmer, bool includeOverview = true)
        {
            var top = 3;

            var crew = JsonConvert.DeserializeObject<CrewModel[]>(movie.Crew)
                .Where(x => string.Equals(x.job, "Director", StringComparison.InvariantCultureIgnoreCase))
                .Take(top)
                .Select(x => $"Crew{x.name.Replace(" ", "")}")
                .ToList();

            var cast = JsonConvert.DeserializeObject<CastBigModel[]>(movie.Cast)
                .OrderBy(x => x.order)
                .Take(top)
                .Select(x => $"Cast{x.name.Replace(" ", "")}")
                .ToList();

            var keywords = JsonConvert.DeserializeObject<IdName[]>(movie.Keywords)
                .Take(top)
                .Select(x => $"Kw{x.name.Replace(" ", "")}")
                .ToList();

            var genres = JsonConvert.DeserializeObject<IdName[]>(movie.Genres)
                .Take(top)
                .Select(x => $"Gen{x.name.Replace(" ", "")}")
                .ToList();

            var text = JoinWords(new string[] {
                JoinWords(crew),
                JoinWords(cast),
                JoinWords(keywords),
                JoinWords(genres)
            });

            if (includeOverview)
            {
                var overviewWords = movie.Overview
                    .RemoveStopWords("en")
                    .Tokenize()
                    .Select(stemmer.Stem)
                    .ToList();

                return JoinWords(new string[] { text, JoinWords(overviewWords) });
            }

            return text;
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

        public class MovieProcessingModel
        {
            public int Id { get; set; }
            public string Crew { get; set; }
            public string Cast { get; set; }
            public string Keywords { get; set; }
            public string Genres { get; set; }
            public string Overview { get; set; }
        }

        private class StructuredDataModel
        {
            public List<string> Crew { get; set; }
            public List<string> Cast { get; set; }
            public List<string> Keywords { get; set; }
            public List<string> Genres { get; set; }
            public List<string> OverviewWords { get; set; }
        }
    }

    public static class ClusteredMovies
    {
        public static Dictionary<int, int> Data { get; set; }
    }

    public class MovieClaster
    {
        [Index(0)]
        public int MovieId { get; set; }

        [Index(1)]
        public int Claster { get; set; }
    }
}
