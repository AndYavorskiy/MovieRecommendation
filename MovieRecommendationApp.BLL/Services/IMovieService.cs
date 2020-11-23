using MovieRecommendationApp.BLL.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MovieRecommendationApp.BLL.Services
{
    public interface IMovieService
    {
        Task<ListData<MovieModel>> Search(MovieSearchFilterModel filter);

        Task<MovieModel> Get(int id);

        Task<List<MovieModel>> GetRecommendations(int id, int top);
        Task<List<GenreModel>> GetGenres();
        Task SetNoPoster(int id);
    }
}