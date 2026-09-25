using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Z.EntityFramework.Extensions;

namespace Z.EntityFramework.Plus
{
    /// <summary>
    /// A second context over the same InMemory database, for the Batch Update / Delete fallback to save through.
    /// A statement changes rows and nothing else: the query's own context keeps its tracked instances as they were,
    /// its pending changes stay pending, and nothing new is tracked.
    /// Saving through the query's context would break all three, so the fallback saves through this one and disposes it.
    /// </summary>
    internal static class InMemoryContext
    {
        public static DbContext Create(DbContext context)
        {
            var newContext = EntityFrameworkManager.ContextFactory?.Invoke(context) ?? CreateFromOptions(context);

            if (ReferenceEquals(newContext, context))
            {
                throw new InvalidOperationException(
                    "EntityFrameworkManager.ContextFactory returned the query's own context. Batch Update / Delete " +
                    "on the InMemory provider need a separate DbContext instance over the same database.");
            }

            return newContext;
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