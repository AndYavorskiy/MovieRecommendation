using Microsoft.EntityFrameworkCore;
using MovieRecommendationApp.DAL.Entities;

namespace MovieRecommendationApp.DAL.Contexts
{
    public class MovieRecommendationDbContext : DbContext
    {
        public DbSet<Movie> Movies { get; set; }

        public MovieRecommendationDbContext(DbContextOptions<MovieRecommendationDbContext> options) : base(options)
        { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(MovieRecommendationDbContext).Assembly);
        }
    }
}
