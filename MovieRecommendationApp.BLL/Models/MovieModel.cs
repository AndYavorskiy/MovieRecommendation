using System;
using System.Collections.Generic;
using System.Text;

namespace MovieRecommendationApp.BLL.Models
{
    public class MovieModel
    {
        public int Id { get; set; }
        public int OriginalId { get; set; }
        public bool Adult { get; set; }
        public decimal? Budget { get; set; }
        public string Genres { get; set; }
        public string ImdbId { get; set; }
        public string OriginalLanguage { get; set; }
        public string OriginalTitle { get; set; }
        public string Overview { get; set; }
        public double Popularity { get; set; }
        public string PosterPath { get; set; }
        public string ProductionCompanies { get; set; }
        public string ProductionCountries { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public decimal? Revenue { get; set; }
        public double? Runtime { get; set; }
        public string SpokenLanguages { get; set; }
        public string Status { get; set; }
        public string Title { get; set; }
        public double VoteAverage { get; set; }
        public double VoteCount { get; set; }

        //Credits
        public string Cast { get; set; }
        public string Crew { get; set; }

        //Keywords
        public string Keywords { get; set; }
    }
}
