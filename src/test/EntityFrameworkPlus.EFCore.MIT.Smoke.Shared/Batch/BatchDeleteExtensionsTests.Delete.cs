using Microsoft.EntityFrameworkCore;
using Z.EntityFramework.Plus;

namespace EntityFrameworkPlus.EFCore.MIT.Smoke.Batch
{
    public partial class BatchDeleteExtensionsTests
    {
        [Theory]
        [MemberData(nameof(Providers))]
        public async Task Delete_ShouldLeaveTrackedInstanceUnchanged_When_ItsRowIsDeleted(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();
            var tracked = await context.Items.SingleAsync(_ => _.Name == "a");

            // Act
            var count = context.Items.Where(_ => _.BufferId == 10).Delete();

            // Assert
            count.ShouldBe(2);

            tracked.BufferId.ShouldBe(10);

            context.Entry(tracked).State.ShouldBe(EntityState.Unchanged);

            context.ChangeTracker.Entries<Item>().ShouldHaveSingleItem().Entity.ShouldBeSameAs(tracked);
        }
    }
}