using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MovieRecommendationApp.DAL.Contexts;

namespace MovieRecommendationApp.DAL
{
    public static class DALModule
    {
        public static void Load(IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<MovieRecommendationDbContext>(x => x.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
        }
    }
}
