using Microsoft.EntityFrameworkCore;
using Z.EntityFramework.Plus;

namespace EntityFrameworkPlus.EFCore.MIT.Smoke;

public class QueryFutureSqlServerTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;

    public QueryFutureSqlServerTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Future_ShouldRunInOneRoundTrip_When_CombinedWithFutureValues(bool retryOnFailure)
    {
        using var context = _fixture.CreateContext(retryOnFailure);
        var counter = context.OpenWithRoundTripCounter();

        var blogs = context.Blogs.OrderBy(blog => blog.Name).Future();
        var highScoringPosts = context.Posts.Where(post => post.Score >= 4).OrderBy(post => post.Score).Future();
        var blogCount = context.Blogs.DeferredCount().FutureValue();
        var firstBlog = context.Blogs.OrderBy(blog => blog.Name).DeferredFirstOrDefault().FutureValue();

        var blogList = await blogs.ToListAsync();

        Assert.Equal(1, counter.RoundTrips);
        Assert.Equal(new[] { "A", "B" }, blogList.Select(blog => blog.Name));
        Assert.Equal(new[] { 4, 5, 6 }, (await highScoringPosts.ToListAsync()).Select(post => post.Score));
        Assert.Equal(2, blogCount.Value);
        Assert.Equal("A", firstBlog.Value.Name);
        Assert.Equal(1, counter.RoundTrips);
    }

    [Fact]
    public async Task Future_ShouldMaterializeIncludedGraph()
    {
        using var context = _fixture.CreateContext();
        var counter = context.OpenWithRoundTripCounter();

        var blogs = context.Blogs.Include(blog => blog.Posts).OrderBy(blog => blog.Name).Future();
        var postCount = context.Posts.DeferredCount().FutureValue();

        var blogList = await blogs.ToListAsync();

        Assert.Equal(1, counter.RoundTrips);
        Assert.Equal(new[] { 3, 2 }, blogList.Select(blog => blog.Posts.Count));
        Assert.Equal(new[] { "A1", "A2", "A3" }, blogList[0].Posts.OrderBy(post => post.Score).Select(post => post.Title));
        Assert.Equal(6, postCount.Value);
    }

    [Fact]
    public async Task Future_ShouldKeepParameterValues_When_QueriesShareParameterNames()
    {
        using var context = _fixture.CreateContext();
        var counter = context.OpenWithRoundTripCounter();

        // Each call captures its own `minimumScore`, so EF Core names both parameters identically;
        // Query Future must rename them apart when it combines the queries.
        var atLeastTwo = ScoresFrom(context, 2).Future();
        var atLeastFive = ScoresFrom(context, 5).Future();
        var atLeastSevenCount = ScoresFrom(context, 7).DeferredCount().FutureValue();

        var atLeastTwoList = await atLeastTwo.ToListAsync();

        Assert.Equal(1, counter.RoundTrips);
        Assert.Equal(new[] { 2, 3, 4, 5, 6 }, atLeastTwoList);
        Assert.Equal(new[] { 5, 6 }, await atLeastFive.ToListAsync());
        Assert.Equal(0, atLeastSevenCount.Value);
    }

    [Fact]
    public async Task Future_ShouldApplyQueryFilter_When_FilterReadsContextProperty()
    {
        using var context = _fixture.CreateContext(tenantId: 2);

        var names = context.Blogs.Select(blog => blog.Name).Future();
        var count = context.Blogs.DeferredCount().FutureValue();

        Assert.Equal(new[] { "C" }, await names.ToListAsync());
        Assert.Equal(1, count.Value);
    }

    [Fact]
    public void Future_ShouldRunInOneRoundTrip_When_EnumeratedSynchronously()
    {
        using var context = _fixture.CreateContext();
        var counter = context.OpenWithRoundTripCounter();

        var names = context.Blogs.OrderBy(blog => blog.Name).Select(blog => blog.Name).Future();
        var count = context.Posts.DeferredCount().FutureValue();

        Assert.Equal(new[] { "A", "B" }, names.ToList());
        Assert.Equal(6, count.Value);
        Assert.Equal(1, counter.RoundTrips);
    }

    [Fact]
    public void FromCache_ShouldNotHitDatabase_When_CalledTwice()
    {
        using var context = _fixture.CreateContext();
        var counter = context.OpenWithRoundTripCounter();

        var first = context.Blogs.OrderBy(blog => blog.Name).FromCache().Select(blog => blog.Name).ToList();
        var roundTripsAfterFirst = counter.RoundTrips;
        var second = context.Blogs.OrderBy(blog => blog.Name).FromCache().Select(blog => blog.Name).ToList();

        Assert.Equal(new[] { "A", "B" }, first);
        Assert.Equal(first, second);
        Assert.Equal(roundTripsAfterFirst, counter.RoundTrips);
    }

    private static IQueryable<int> ScoresFrom(SmokeDbContext context, int minimumScore)
    {
        return context.Posts.Where(post => post.Score >= minimumScore).OrderBy(post => post.Score).Select(post => post.Score);
    }
}