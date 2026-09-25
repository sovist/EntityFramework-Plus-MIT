using Microsoft.EntityFrameworkCore;
using Z.EntityFramework.Extensions;
using Z.EntityFramework.Plus;

namespace EntityFrameworkPlus.EFCore.MIT.Smoke.Batch
{
    public partial class BatchDeleteExtensionsTests
    {
        [Theory]
        [MemberData(nameof(Providers))]
        public async Task DeleteAsync_ShouldDeleteRows_When_RowsMatch(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();

            // Act
            var count = await context.Items.Where(_ => _.BufferId == 10).DeleteAsync();

            // Assert
            count.ShouldBe(2);

            (await database.LoadItems()).ShouldHaveSingleItem().Name.ShouldBe("c");
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task DeleteAsync_ShouldDeleteRowsReachedThroughJoin(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();
            var ids = context.Items.Where(_ => _.BufferId == 10).Select(_ => _.Id);

            // Act
            var count = await ids
                .Join(context.Items, id => id, item => item.Id, (id, item) => item)
                .DeleteAsync();

            // Assert
            count.ShouldBe(2);

            (await database.LoadItems()).ShouldHaveSingleItem().Name.ShouldBe("c");
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task DeleteAsync_ShouldReturnZero_When_NoRowsMatch(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();

            // Act
            var count = await context.Items.Where(_ => _.BufferId == 99).DeleteAsync();

            // Assert
            count.ShouldBe(0);

            (await database.LoadItems()).Count.ShouldBe(3);
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task DeleteAsync_ShouldPersistWithoutSaveChanges_And_LoadNothing(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();

            // Act
            var count = await context.Items.Where(_ => _.BufferId == 10).DeleteAsync();

            // Assert
            count.ShouldBe(2);

            context.ChangeTracker.Entries().ShouldBeEmpty();

            (await database.LoadItems()).ShouldHaveSingleItem().Name.ShouldBe("c");
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task DeleteAsync_ShouldLeaveTrackedInstanceUnchanged_When_ItsRowIsDeleted(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();
            var tracked = await context.Items.SingleAsync(_ => _.Name == "a");

            // Act
            await context.Items.Where(_ => _.BufferId == 10).DeleteAsync();

            // Assert
            tracked.BufferId.ShouldBe(10);

            context.Entry(tracked).State.ShouldBe(EntityState.Unchanged);

            (await database.LoadItems()).ShouldHaveSingleItem().Name.ShouldBe("c");
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task DeleteAsync_ShouldLeaveOtherPendingChangesUnsaved(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();
            var pending = await context.Items.SingleAsync(_ => _.Name == "c");
            pending.Name = "c-pending";

            // Act
            var count = await context.Items.Where(_ => _.BufferId == 10).DeleteAsync();

            // Assert
            count.ShouldBe(2);

            context.Entry(pending).State.ShouldBe(EntityState.Modified);

            (await database.LoadItems()).ShouldHaveSingleItem().Name.ShouldBe("c");
        }

        [Fact]
        public async Task DeleteAsync_ShouldLeaveContextFromContextFactoryUsable_When_FactoryHandsOutTheSameInstance()
        {
            using var database = await TestDatabase.Create(Provider.InMemory, Items());
            using var context = database.CreateContext();
            using var shared = database.CreateContext();

            // A container-owned second context: the same instance for every call, disposed by its owner, not here.
            EntityFrameworkManager.ContextFactory = current => ReferenceEquals(current, context) ? shared : null;

            try
            {
                // Act
                var first = await context.Items.Where(_ => _.BufferId == 10).DeleteAsync();

                var second = await context.Items.Where(_ => _.BufferId == 20).DeleteAsync();

                // Assert
                first.ShouldBe(2);

                second.ShouldBe(1);

                shared.ChangeTracker.Entries().ShouldBeEmpty();

                (await shared.Items.CountAsync()).ShouldBe(0);

                (await database.LoadItems()).ShouldBeEmpty();
            }
            finally
            {
                EntityFrameworkManager.ContextFactory = null;
            }
        }
    }
}