﻿using System.Collections.Generic;

namespace MovieRecommendationApp.BLL.Models
{
    public class MovieSearchFilter
    {
        public string FilterText { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
        public int[] Genres { get; set; }
    }
}
