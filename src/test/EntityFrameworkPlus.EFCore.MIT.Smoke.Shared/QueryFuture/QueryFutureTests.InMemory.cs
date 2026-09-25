using Microsoft.EntityFrameworkCore;
using Z.EntityFramework.Plus;

namespace EntityFrameworkPlus.EFCore.MIT.Smoke.QueryFuture
{
    /// <summary>
    /// On the InMemory provider Query Future detects the provider (through the shim's <c>GetQueryContextFactory</c>)
    /// and runs each query on its own instead of combining them. Results must still be right.
    /// </summary>
    public class QueryFutureInMemoryTests
    {
        [Fact]
        public async Task Future_ShouldReturnResults_When_ProviderIsInMemory()
        {
            using var context = CreateContext(nameof(Future_ShouldReturnResults_When_ProviderIsInMemory));
            var blogs = context.Blogs.OrderBy(blog => blog.Name).Future();
            var postCount = context.Posts.DeferredCount().FutureValue();
            var firstBlog = context.Blogs.OrderBy(blog => blog.Name).DeferredFirstOrDefault().FutureValue();

            // Act
            var blogList = await blogs.ToListAsync();

            // Assert
            blogList.ShouldBeInOrder(blog => blog.Name, "A", "B");
            postCount.Value.ShouldBe(6);
            firstBlog.Value.Name.ShouldBe("A");
        }

        private static SmokeDbContext CreateContext(string databaseName)
        {
            var options = new DbContextOptionsBuilder<SmokeDbContext>().UseInMemoryDatabase(databaseName).Options;
            var context = new SmokeDbContext(options);

            if (!context.Blogs.IgnoreQueryFilters().Any())
            {
                SmokeDbContext.Seed(context);
            }

            return context;
        }
    }
}