using Microsoft.EntityFrameworkCore;
using MovieRecommendationApp.DAL.Entities;

namespace MovieRecommendationApp.DAL.Contexts
{
    public class MovieRecommendationDbContext : DbContext
    {
        public DbSet<Movie> Movies { get; set; }
        public DbSet<Genre> Genres { get; set; }
        public DbSet<MovieGenre> MoviesGenres { get; set; }

        public MovieRecommendationDbContext(DbContextOptions<MovieRecommendationDbContext> options) : base(options)
        { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(MovieRecommendationDbContext).Assembly);

            modelBuilder.Entity<MovieGenre>().HasKey(x => new { x.MovieId, x.GenreId });

            modelBuilder.Entity<MovieGenre>()
                .HasOne(bc => bc.Genre)
                .WithMany(b => b.MoviesGenres)
                .HasForeignKey(bc => bc.GenreId);

            modelBuilder.Entity<MovieGenre>()
                .HasOne(bc => bc.Movie)
                .WithMany(c => c.MoviesGenres)
                .HasForeignKey(bc => bc.MovieId);
        }
    }
}
