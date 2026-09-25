using Z.EntityFramework.Plus;

namespace EntityFrameworkPlus.EFCore.MIT.Smoke.Batch
{
    public partial class BatchUpdateExtensionsTests
    {
        [Theory]
        [MemberData(nameof(Providers))]
        public async Task Update_ShouldSetAssignedMembers_When_RowsMatch(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();

            // Act
            var count = context.Items
                .Where(_ => _.BufferId == 10)
                .Update(_ => new Item { BufferId = null });

            // Assert
            count.ShouldBe(2);

            (await database.LoadItems()).ShouldBeInOrder(_ => _.BufferId, null, null, 20);
        }
    }
}