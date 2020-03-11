using System.Collections.Generic;

namespace MovieRecommendationApp.BLL.Models
{
    public class MovieSearchFilterModel
    {
        public string FilterText { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
        public int[] Genres { get; set; }
    }
}
