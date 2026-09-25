using Microsoft.EntityFrameworkCore;
using Z.EntityFramework.Plus;

namespace EntityFrameworkPlus.EFCore.MIT.Smoke.QueryFuture
{
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

            // Act
            var blogList = await blogs.ToListAsync();

            // Assert
            counter.RoundTrips.ShouldBe(1);
            blogList.ShouldBeInOrder(blog => blog.Name, "A", "B");
            (await highScoringPosts.ToListAsync()).ShouldBeInOrder(post => post.Score, 4, 5, 6);
            blogCount.Value.ShouldBe(2);
            firstBlog.Value.Name.ShouldBe("A");
            counter.RoundTrips.ShouldBe(1);
        }

        [Fact]
        public async Task Future_ShouldMaterializeIncludedGraph()
        {
            using var context = _fixture.CreateContext();
            var counter = context.OpenWithRoundTripCounter();

            var blogs = context.Blogs.Include(blog => blog.Posts).OrderBy(blog => blog.Name).Future();
            var postCount = context.Posts.DeferredCount().FutureValue();

            // Act
            var blogList = await blogs.ToListAsync();

            // Assert
            counter.RoundTrips.ShouldBe(1);
            blogList.ShouldBeInOrder(blog => blog.Posts.Count, 3, 2);
            blogList[0].Posts.OrderBy(post => post.Score).ShouldBeInOrder(post => post.Title, "A1", "A2", "A3");
            postCount.Value.ShouldBe(6);
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

            // Act
            var atLeastTwoList = await atLeastTwo.ToListAsync();

            // Assert
            counter.RoundTrips.ShouldBe(1);
            atLeastTwoList.ShouldBeInOrder(2, 3, 4, 5, 6);
            (await atLeastFive.ToListAsync()).ShouldBeInOrder(5, 6);
            atLeastSevenCount.Value.ShouldBe(0);
        }

        [Fact]
        public async Task Future_ShouldApplyQueryFilter_When_FilterReadsContextProperty()
        {
            using var context = _fixture.CreateContext(tenantId: 2);
            var names = context.Blogs.Select(blog => blog.Name).Future();
            var count = context.Blogs.DeferredCount().FutureValue();

            // Act
            var nameList = await names.ToListAsync();

            // Assert
            nameList.ShouldBeInOrder("C");
            count.Value.ShouldBe(1);
        }

        [Fact]
        public void Future_ShouldRunInOneRoundTrip_When_EnumeratedSynchronously()
        {
            using var context = _fixture.CreateContext();
            var counter = context.OpenWithRoundTripCounter();
            var names = context.Blogs.OrderBy(blog => blog.Name).Select(blog => blog.Name).Future();
            var count = context.Posts.DeferredCount().FutureValue();

            // Act
            var nameList = names.ToList();

            // Assert
            nameList.ShouldBeInOrder("A", "B");
            count.Value.ShouldBe(6);
            counter.RoundTrips.ShouldBe(1);
        }

        [Fact]
        public void FromCache_ShouldNotHitDatabase_When_CalledTwice()
        {
            using var context = _fixture.CreateContext();
            var counter = context.OpenWithRoundTripCounter();

            // Act
            var first = context.Blogs.OrderBy(blog => blog.Name).FromCache().Select(blog => blog.Name).ToList();
            var roundTripsAfterFirst = counter.RoundTrips;
            var second = context.Blogs.OrderBy(blog => blog.Name).FromCache().Select(blog => blog.Name).ToList();

            // Assert
            first.ShouldBeInOrder("A", "B");
            second.ShouldBe(first);
            counter.RoundTrips.ShouldBe(roundTripsAfterFirst);
        }

        private static IQueryable<int> ScoresFrom(SmokeDbContext context, int minimumScore)
        {
            return context.Posts.Where(post => post.Score >= minimumScore).OrderBy(post => post.Score).Select(post => post.Score);
        }
    }
}