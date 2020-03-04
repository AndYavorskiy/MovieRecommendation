using CsvHelper;
using EFCore.BulkExtensions;
using MovieRecommendationApp.BLL.ParseModels;
using MovieRecommendationApp.DAL.Contexts;
using MovieRecommendationApp.DAL.Entities;
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
        private readonly MovieRecommendationDbContext dbContext;

        public DataParserService(MovieRecommendationDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task ParseData()
        {
            var movies = GetMoviesBig();
            var credits = GetCreditsBig();
            var keywords = GetKeywordsBig();

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
                .ToList();

            await dbContext.BulkInsertAsync(moviesToSave);
        }

        private static List<MovieModelBig> GetMoviesBig()
        {
            var movies = new List<MovieModelBig>();


            using (var reader = new StreamReader(GetPath("movies_metadata.csv")))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Configuration.Delimiter = ",";
                csv.Configuration.HasHeaderRecord = true;

                movies = csv.GetRecords<MovieModelBig>().ToList();
            }

            return movies
                .OrderBy(x => x.id)
                .ToList();
        }

        private static List<CreditsBigModel> GetCreditsBig()
        {
            var credits = new List<CreditsBigModel>();

            using (var reader = new StreamReader(GetPath("credits.csv")))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Configuration.Delimiter = ",";
                csv.Configuration.HasHeaderRecord = true;

                credits = csv.GetRecords<CreditsBigModel>().ToList();
            }

            return credits;
        }

        private static List<KeywordsBigModel> GetKeywordsBig()
        {
            var keywords = new List<KeywordsBigModel>();

            using (var reader = new StreamReader(GetPath("keywords.csv")))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Configuration.Delimiter = ",";
                csv.Configuration.HasHeaderRecord = true;

                keywords = csv.GetRecords<KeywordsBigModel>().ToList();
            }

            return keywords;
        }

        private static string GetPath(string fileName)
        {
            return Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "DataSources", fileName);
        }
    }
}
