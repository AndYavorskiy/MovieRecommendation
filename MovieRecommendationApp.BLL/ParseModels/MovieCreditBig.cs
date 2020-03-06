using System;

namespace MovieRecommendationApp.BLL.ParseModels
{
    public class MovieCreditBig
    {
        //Movie
        public int id { get; set; }
        public bool adult { get; set; }
        public decimal? budget { get; set; }
        public IdName[] genres { get; set; }
        public string imdb_id { get; set; }
        public string original_language { get; set; }
        public string original_title { get; set; }
        public string overview { get; set; }
        public double popularity { get; set; }
        public string poster_path { get; set; }
        public string production_companies { get; set; }
        public string production_countries { get; set; }
        public DateTime? release_date { get; set; }
        public decimal? revenue { get; set; }
        public double? runtime { get; set; }
        public string spoken_languages { get; set; }
        public string status { get; set; }
        public string title { get; set; }
        public double vote_average { get; set; }
        public double vote_count { get; set; }

        //Credits
        public CastBigModel[] cast { get; set; }
        public CrewModel[] crew { get; set; }

        //Keywords
        public IdName[] keywords { get; set; }
    }
}
