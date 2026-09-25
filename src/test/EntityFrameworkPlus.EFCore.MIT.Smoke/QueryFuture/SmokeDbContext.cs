using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkPlus.EFCore.MIT.Smoke.QueryFuture
{
    public class Blog
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public int TenantId { get; set; }

        public List<Post> Posts { get; set; } = new();
    }

    public class Post
    {
        public int Id { get; set; }

        public int BlogId { get; set; }

        public Blog Blog { get; set; }

        public string Title { get; set; }

        public int Score { get; set; }
    }

    public class SmokeDbContext : DbContext
    {
        public SmokeDbContext(DbContextOptions<SmokeDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        /// Read by the global query filter on <see cref="Blog"/>, so every Blog query carries an EF Core runtime
        /// parameter — the path <c>InsertRuntimeParameters</c> in the shim exists for.
        /// </summary>
        public int TenantId { get; set; } = 1;

        public DbSet<Blog> Blogs => Set<Blog>();

        public DbSet<Post> Posts => Set<Post>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Blog>().HasQueryFilter(blog => blog.TenantId == TenantId);
        }

        /// <summary>Tenant 1 owns blogs A (posts scoring 1, 2, 3) and B (4, 5); tenant 2 owns blog C (6).</summary>
        public static void Seed(SmokeDbContext context)
        {
            context.Blogs.AddRange(
                new Blog { Name = "A", TenantId = 1, Posts = { new Post { Title = "A1", Score = 1 }, new Post { Title = "A2", Score = 2 }, new Post { Title = "A3", Score = 3 } } },
                new Blog { Name = "B", TenantId = 1, Posts = { new Post { Title = "B1", Score = 4 }, new Post { Title = "B2", Score = 5 } } },
                new Blog { Name = "C", TenantId = 2, Posts = { new Post { Title = "C1", Score = 6 } } });

            context.SaveChanges();
        }
    }
}