using System.Collections.Generic;

namespace MovieRecommendationApp.BLL.Models
{
    public class ListData<T>
    {
        public int TotalCount { get; set; }
        public List<T> Items { get; set; }
    }
}
