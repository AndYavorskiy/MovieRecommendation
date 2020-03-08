using Accord.MachineLearning;
using Accord.Math.Distances;
using Accord.Statistics.Models.Regression;
using Accord.Statistics.Models.Regression.Fitting;
using CsvHelper;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MovieRecommendationApp.BLL.ParseModels;
using MovieRecommendationApp.DAL.Contexts;
using MovieRecommendationApp.DAL.Entities;
using Newtonsoft.Json;
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

            var moviesToSave = movies
                .Where(x => x.vote_average.HasValue
                        && x.vote_count.HasValue
                        && x.popularity.HasValue
                        && !x.video)
                .Where(x => x.status == "Released")
                .Where(x => x.popularity > 1)
                .Where(x => x.release_date.HasValue)
                .Join(credits,
                x => x.id,
                x => x.id,
                (movie, credit) =>
                {
                    Movie movieCreditBig = new Movie
                    {
                        OriginalId = movie.id,
                        Adult = movie.adult,
                        Budget = movie.budget,
                        Genres = movie.genres.Replace(@"\xa", " ").Replace(@"\\x92", "'").Replace(@"\x", " ").Replace(": None", ": 'None'"),
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
                        Crew = credit.crew.Replace(@"\xa", " ").Replace(@"\\x92", "'").Replace(@"\x", " ").Replace(": None", ": 'None'"),
                        Cast = credit.cast.Replace(@"\xa", " ").Replace(@"\\x92", "'").Replace(@"\x", " ").Replace(": None", ": 'None'")
                    };

                    var kWords = keywords.FirstOrDefault(x => x.id == movie.id);

                    if (kWords != null)
                    {
                        movieCreditBig.Keywords = kWords.keywords.Replace(@"\xa", " ");
                    }

                    return movieCreditBig;
                })
                .OrderByDescending(x => x.ReleaseDate)
                .Take(5000)
                .ToList();

            var g = moviesToSave.GroupBy(x => x.Title).ToList();

            var gm = g.Where(x => x.Count() > 1).ToList();

            await dbContext.Movies.BatchDeleteAsync();

            await dbContext.BulkInsertAsync(moviesToSave);
        }

        public async Task GenerateCreditsGenresKeywordsCastSimilarityMatrix()
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

            var words = movies.Select(GetMovieData).ToArray().Tokenize();

            var codebook = new TFIDF()
            {
                Tf = TermFrequency.Log,
                Idf = InverseDocumentFrequency.Default
            };

            codebook.Learn(words);

            var bow = codebook.Transform(words);

            // Lets create a Logistic classifier to separate the two paragraphs:
            var learner = new IterativeReweightedLeastSquares<LogisticRegression>()
            {
                Tolerance = 1e-4,  // Let's set some convergence parameters
                MaxIterations = 100,  // maximum number of iterations to perform
                Regularization = 0
            };

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

        private static string GetMovieData(MovieProcessingModel movie)
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

            return $"{JoinWords(crew)} {JoinWords(cast)} {JoinWords(keywords)} {JoinWords(genres)} {movie.Overview}";
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
            var moviesCount = await dbContext.Movies.CountAsync();
            for (int i = 0; i < moviesCount; i++)
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
