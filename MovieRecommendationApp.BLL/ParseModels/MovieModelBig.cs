using System;

namespace MovieRecommendationApp.BLL.ParseModels
{
    public class MovieModelBig
    {
        public bool adult { get; set; }
        public string belongs_to_collection { get; set; }
        public decimal? budget { get; set; }
        public string genres { get; set; }
        public string homepage { get; set; }
        public int id { get; set; }
        public string imdb_id { get; set; }
        public string original_language { get; set; }
        public string original_title { get; set; }
        public string overview { get; set; }
        public double? popularity { get; set; }
        public string poster_path { get; set; }
        public string production_companies { get; set; }
        public string production_countries { get; set; }
        public DateTime? release_date { get; set; }
        public decimal? revenue { get; set; }
        public double? runtime { get; set; }
        public string spoken_languages { get; set; }
        public string status { get; set; }
        public string tagline { get; set; }
        public string title { get; set; }
        public bool video { get; set; }
        public double? vote_average { get; set; }
        public double? vote_count { get; set; }
    }
}
