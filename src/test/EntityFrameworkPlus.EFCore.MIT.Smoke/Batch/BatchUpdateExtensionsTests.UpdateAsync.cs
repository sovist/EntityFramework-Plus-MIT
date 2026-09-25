using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Z.EntityFramework.Extensions;
using Z.EntityFramework.Plus;

namespace EntityFrameworkPlus.EFCore.MIT.Smoke.Batch
{
    public partial class BatchUpdateExtensionsTests
    {
        [Theory]
        [MemberData(nameof(Providers))]
        public async Task UpdateAsync_ShouldSetAssignedMembers_When_RowsMatch(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();
            var deleteAfter = DeleteAfter;

            // Act
            var count = await context.Items
                .Where(_ => _.BufferId == 10)
                .UpdateAsync(_ => new Item { BufferId = null, DeleteAfter = deleteAfter });

            // Assert
            count.ShouldBe(2);

            var items = await database.LoadItems();

            items.ShouldBeInOrder(_ => _.Name, "a", "b", "c");

            items.ShouldBeInOrder(_ => _.Position, 1, 2, 3);

            items.ShouldBeInOrder(_ => _.BufferId, null, null, 20);

            items.ShouldBeInOrder(_ => _.DeleteAfter, deleteAfter, deleteAfter, null);
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task UpdateAsync_ShouldAssignConstantToNullableMember(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();

            // Act
            var count = await context.Items
                .Where(_ => _.BufferId == 10)
                .UpdateAsync(_ => new Item { BufferId = ArchiveBufferId });

            // Assert
            count.ShouldBe(2);

            (await database.LoadItems()).ShouldBeInOrder(_ => _.BufferId, ArchiveBufferId, ArchiveBufferId, 20);
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task UpdateAsync_ShouldReadTheRowBeingUpdated(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();

            // Act
            var count = await context.Items.UpdateAsync(_ => new Item { Position = _.Position * 10 });

            // Assert
            count.ShouldBe(3);

            (await database.LoadItems()).ShouldBeInOrder(_ => _.Position, 10, 20, 30);
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task UpdateAsync_ShouldUpdateRowsReachedThroughJoin(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();
            var ids = context.Items.Where(_ => _.BufferId == 10).Select(_ => _.Id);
            var deleteAfter = DeleteAfter;

            // Act
            var count = await ids
                .Join(context.Items, id => id, item => item.Id, (id, item) => item)
                .UpdateAsync(_ => new Item { DeleteAfter = deleteAfter });

            // Assert
            count.ShouldBe(2);

            (await database.LoadItems()).ShouldBeInOrder(_ => _.DeleteAfter, deleteAfter, deleteAfter, null);
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task UpdateAsync_ShouldReturnZero_When_NoRowsMatch(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();

            // Act
            var count = await context.Items
                .Where(_ => _.BufferId == 99)
                .UpdateAsync(_ => new Item { BufferId = null });

            // Assert
            count.ShouldBe(0);

            (await database.LoadItems()).ShouldBeInOrder(_ => _.BufferId, 10, 10, 20);
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task UpdateAsync_ShouldPersistWithoutSaveChanges_And_LoadNothing(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();

            // Act
            var count = await context.Items
                .Where(_ => _.BufferId == 10)
                .UpdateAsync(_ => new Item { BufferId = null });

            // Assert
            count.ShouldBe(2);

            context.ChangeTracker.Entries().ShouldBeEmpty();

            (await database.LoadItems()).ShouldBeInOrder(_ => _.BufferId, null, null, 20);
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task UpdateAsync_ShouldLeaveTrackedInstanceUnchanged_When_ItsRowIsUpdated(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();
            var tracked = await context.Items.SingleAsync(_ => _.Name == "a");

            // Act
            await context.Items
                .Where(_ => _.BufferId == 10)
                .UpdateAsync(_ => new Item { BufferId = null });

            // Assert
            tracked.BufferId.ShouldBe(10);

            context.Entry(tracked).State.ShouldBe(EntityState.Unchanged);

            (await database.LoadItems()).ShouldBeInOrder(_ => _.BufferId, null, null, 20);
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task UpdateAsync_ShouldThrow_When_FactoryIsNotAnObjectInitializer(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();

            // Act
            var exception = await Should.ThrowAsync<ArgumentException>(() => context.Items.UpdateAsync(item => item));

            // Assert
            exception.Message.ShouldContain("object initializer");
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task UpdateAsync_ShouldLeaveOtherPendingChangesUnsaved(Provider provider)
        {
            using var database = await TestDatabase.Create(provider, Items());
            using var context = database.CreateContext();
            var pending = await context.Items.SingleAsync(_ => _.Name == "c");
            pending.Name = "c-pending";

            // Act
            var count = await context.Items
                .Where(_ => _.BufferId == 10)
                .UpdateAsync(_ => new Item { BufferId = null });

            // Assert
            count.ShouldBe(2);

            context.Entry(pending).State.ShouldBe(EntityState.Modified);

            var items = await database.LoadItems();
            items.ShouldBeInOrder(_ => _.Name, "a", "b", "c");
            items.ShouldBeInOrder(_ => _.BufferId, null, null, 20);
        }

        [Fact]
        public async Task UpdateAsync_ShouldSaveThroughContextFactory_When_ProviderIsInMemory()
        {
            using var database = await TestDatabase.Create(Provider.InMemory, Items());
            using var context = database.CreateContext();
            var seen = new ConcurrentBag<DbContext>();

            // Test classes run in parallel, so while the factory is set it must stay valid for any context that
            // calls it: a sibling for its own context type, null (the default path) for every other.
            EntityFrameworkManager.ContextFactory = current =>
            {
                seen.Add(current);

                return current is ItemsDbContext
                    ? new ItemsDbContext((DbContextOptions<ItemsDbContext>)current.GetService<IDbContextOptions>())
                    : null;
            };

            try
            {
                // Act
                var count = await context.Items
                    .Where(_ => _.BufferId == 10)
                    .UpdateAsync(_ => new Item { BufferId = null });

                // Assert
                count.ShouldBe(2);

                seen.ShouldContain(context);

                (await database.LoadItems()).ShouldBeInOrder(_ => _.BufferId, null, null, 20);
            }
            finally
            {
                EntityFrameworkManager.ContextFactory = null;
            }
        }

        [Fact]
        public async Task UpdateAsync_ShouldThrow_When_ContextFactoryReturnsTheQueryContext()
        {
            using var database = await TestDatabase.Create(Provider.InMemory, Items());
            using var context = database.CreateContext();

            // Misbehaves for this test's context only; any other caller gets the default path.
            EntityFrameworkManager.ContextFactory = current => ReferenceEquals(current, context) ? current : null;

            try
            {
                // Act
                var exception = await Should.ThrowAsync<InvalidOperationException>(
                    () => context.Items.UpdateAsync(_ => new Item { BufferId = null }));

                // Assert
                exception.Message.ShouldContain("query's own context");

                (await database.LoadItems()).ShouldBeInOrder(_ => _.BufferId, 10, 10, 20);
            }
            finally
            {
                EntityFrameworkManager.ContextFactory = null;
            }
        }

        [Fact]
        public async Task UpdateAsync_ShouldThrow_When_ContextHasNoOptionsConstructor_And_NoContextFactory()
        {
            var options = new DbContextOptionsBuilder<ItemsDbContextNeedingMore>()
                .UseInMemoryDatabase($"batch:{Guid.NewGuid()}")
                .Options;

            using var context = new ItemsDbContextNeedingMore(options, owner: nameof(BatchUpdateExtensionsTests));
            context.Items.AddRange(Items());
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Act
            var exception = await Should.ThrowAsync<InvalidOperationException>(
                () => context.Items.UpdateAsync(_ => new Item { BufferId = null }));

            // Assert
            exception.Message.ShouldContain(nameof(ItemsDbContextNeedingMore));

            exception.Message.ShouldContain("ContextFactory");
        }

        /// <summary>A context the fallback cannot build from options alone, which is what the hook is for.</summary>
        private sealed class ItemsDbContextNeedingMore : DbContext
        {
            public ItemsDbContextNeedingMore(DbContextOptions<ItemsDbContextNeedingMore> options, string owner)
                : base(options)
            {
                Owner = owner;
            }

            public string Owner { get; }

            public DbSet<Item> Items => Set<Item>();
        }
    }
}