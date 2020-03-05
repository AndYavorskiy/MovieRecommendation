using MovieRecommendationApp.BLL.ParseModels;
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
        public IdName[] Genres { get; set; }
        public string ImdbId { get; set; }
        public string OriginalLanguage { get; set; }
        public string OriginalTitle { get; set; }
        public string Overview { get; set; }
        public double Popularity { get; set; }
        public string PosterPath { get; set; }
        public IdName[] Companies { get; set; }
        public IsoName[] Countries { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public decimal? Revenue { get; set; }
        public double? Runtime { get; set; }
        public IsoName[] Languages { get; set; }
        public string Status { get; set; }
        public string Title { get; set; }
        public double VoteAverage { get; set; }
        public double VoteCount { get; set; }

        //Credits
        public CharacterActorModel[] Cast { get; set; }
        public string[] Directors { get; set; }

        //Keywords
        public IdName[] Keywords { get; set; }
    }
}
