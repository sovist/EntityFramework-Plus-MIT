using Microsoft.EntityFrameworkCore;
using Z.EntityFramework.Plus;

namespace EntityFramework.Plus.EFCore.MIT.Smoke;

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

        Assert.Equal(new[] { "A", "B" }, (await blogs.ToListAsync()).Select(blog => blog.Name));
        Assert.Equal(6, postCount.Value);
        Assert.Equal("A", firstBlog.Value.Name);
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