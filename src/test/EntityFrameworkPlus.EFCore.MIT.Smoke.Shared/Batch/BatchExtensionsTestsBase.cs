using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkPlus.EFCore.MIT.Smoke.Batch
{
    /// <summary>
    /// Batch Update / Batch Delete have two halves: the translation to <c>ExecuteUpdate</c> / <c>ExecuteDelete</c>
    /// for relational providers, and the load-and-save fallback for the InMemory provider. Every theory runs
    /// against both, with SQLite standing in for the relational half, so the fallback is held to what a real
    /// statement does - including what it does <em>not</em> do to the context's tracked instances.
    /// </summary>
    public abstract class BatchExtensionsTestsBase
    {
        /// <summary>
        /// Tests that assign the process-wide <c>EntityFrameworkManager.ContextFactory</c> share this xunit collection,
        /// so no two of them run at once: a test setting or clearing the static while another one runs would hand that
        /// test's query a different second context than the one it arranged.
        /// </summary>
        public const string ContextFactoryCollection = "EntityFrameworkManager.ContextFactory";

        public enum Provider
        {
            InMemory,
            Sqlite,
        }

        public static TheoryData<Provider> Providers => new() { Provider.InMemory, Provider.Sqlite };

        /// <summary>Three items, two of which share buffer 10, so a predicate on the buffer matches a strict subset.</summary>
        protected static Item[] Items() =>
        [
            new Item { Name = "a", Position = 1, BufferId = 10 },
            new Item { Name = "b", Position = 2, BufferId = 10 },
            new Item { Name = "c", Position = 3, BufferId = 20 },
        ];

        public class Item
        {
            public int Id { get; set; }

            public string Name { get; set; }

            public int Position { get; set; }

            public int? BufferId { get; set; }

            public DateTime? DeleteAfter { get; set; }
        }

        public class ItemsDbContext : DbContext
        {
            public ItemsDbContext(DbContextOptions<ItemsDbContext> options)
                : base(options)
            {
            }

            public DbSet<Item> Items => Set<Item>();
        }

        /// <summary>
        /// One database per test, seeded once and read back through fresh contexts,
        /// so an assertion sees what the database holds rather than what a context still tracks.
        /// </summary>
        public sealed class TestDatabase : IDisposable
        {
            private readonly DbContextOptions<ItemsDbContext> _options;

            private readonly SqliteConnection _connection;

            private TestDatabase(DbContextOptions<ItemsDbContext> options, SqliteConnection connection = null)
            {
                _options = options;
                _connection = connection;
            }

            public static async Task<TestDatabase> Create(Provider provider, params Item[] items)
            {
                var database = provider switch
                {
                    Provider.InMemory => InMemory(),
                    Provider.Sqlite => Sqlite(),
                    _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
                };

                using var context = database.CreateContext();
                await context.Database.EnsureCreatedAsync();
                context.Items.AddRange(items);
                await context.SaveChangesAsync();

                return database;
            }

            private static TestDatabase InMemory()
            {
                var options = new DbContextOptionsBuilder<ItemsDbContext>()
                    .UseInMemoryDatabase($"batch:{Guid.NewGuid()}")
                    .Options;

                return new TestDatabase(options);
            }

            private static TestDatabase Sqlite()
            {
                // An in-memory SQLite database lives as long as its connection, so the connection is opened
                // here and closed with the database rather than with any one context.
                var connection = new SqliteConnection("DataSource=:memory:");
                connection.Open();

                var options = new DbContextOptionsBuilder<ItemsDbContext>()
                    .UseSqlite(connection)
                    .Options;

                return new TestDatabase(options, connection);
            }

            public ItemsDbContext CreateContext() => new(_options);

            public async Task<List<Item>> LoadItems()
            {
                using var context = CreateContext();

                return await context.Items.OrderBy(_ => _.Position).ToListAsync();
            }

            public void Dispose() => _connection?.Dispose();
        }
    }
}