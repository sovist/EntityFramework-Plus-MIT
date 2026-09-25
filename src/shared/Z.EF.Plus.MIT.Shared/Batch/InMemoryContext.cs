using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Z.EntityFramework.Extensions;

namespace Z.EntityFramework.Plus
{
    /// <summary>
    /// A second context over the same InMemory database, for the Batch Update / Delete fallback to save through.
    /// A statement changes rows and nothing else: the query's own context keeps its tracked instances as they were,
    /// its pending changes stay pending, and nothing new is tracked.
    /// Saving through the query's context would break all three, so the fallback saves through this one.
    /// </summary>
    /// <remarks>
    /// Who owns the context decides what happens to it afterwards. One this class built from the query context's
    /// options is disposed. One <see cref="EntityFrameworkManager.ContextFactory"/> returned belongs to the factory's
    /// owner - a container commonly hands out the same instance for a whole scope, and the next batch call gets it
    /// again - so it is not disposed; instead the rows this call attached are detached, and the context goes back
    /// as clean as it came.
    /// </remarks>
    internal sealed class InMemoryContext : IDisposable
    {
        private readonly DbContext _context;

        private readonly bool _owned;

        private readonly List<object> _attached = [];

        private InMemoryContext(DbContext context, bool owned)
        {
            _context = context;
            _owned = owned;
        }

        public static InMemoryContext Create(DbContext context)
        {
            var fromFactory = EntityFrameworkManager.ContextFactory?.Invoke(context);

            if (fromFactory == null)
            {
                return new InMemoryContext(CreateFromOptions(context), owned: true);
            }

            if (ReferenceEquals(fromFactory, context))
            {
                throw new InvalidOperationException(
                    "EntityFrameworkManager.ContextFactory returned the query's own context. Batch Update / Delete " +
                    "on the InMemory provider need a separate DbContext instance over the same database.");
            }

            return new InMemoryContext(fromFactory, owned: false);
        }

        /// <summary>
        /// Attaches the row alone, in the given state, and remembers it. <c>Entry(...).State</c> touches only
        /// that row; <c>Attach</c>, <c>Update</c> and <c>Remove</c> would walk its navigations too.
        /// </summary>
        public EntityEntry Attach(object entity, EntityState state)
        {
            var entry = _context.Entry(entity);

            entry.State = state;

            _attached.Add(entity);

            return entry;
        }

        public int SaveChanges() => _context.SaveChanges();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => _context.SaveChangesAsync(cancellationToken);

        public void Dispose()
        {
            if (_owned)
            {
                _context.Dispose();

                return;
            }

            foreach (var entity in _attached)
            {
                _context.Entry(entity).State = EntityState.Detached;
            }
        }

        /// <summary>
        /// The context's own type, constructed from the options it runs with: the constructor an EF Core
        /// context has by convention. A context that needs more is what <see cref="EntityFrameworkManager.ContextFactory"/> is for.
        /// </summary>
        private static DbContext CreateFromOptions(DbContext context)
        {
            var type = context.GetType();

            var options = context.GetService<IDbContextOptions>();

            var constructor = type
                .GetConstructors()
                .FirstOrDefault(candidate => candidate.GetParameters() is { Length: 1 } parameters && parameters[0].ParameterType.IsInstanceOfType(options));

            if (constructor == null)
            {
                throw new InvalidOperationException(
                    $"Batch Update / Delete on the InMemory provider save through a second {type.Name} over the same database, " +
                    $"and {type.Name} has no constructor taking its DbContextOptions. " +
                    "Set Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory to create one.");
            }

            return (DbContext)constructor.Invoke([options]);
        }
    }
}