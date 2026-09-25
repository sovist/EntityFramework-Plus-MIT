using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Z.EntityFramework.Plus
{
    // The InMemory provider supports neither ExecuteUpdate nor ExecuteDelete - it throws rather than falling back.
    // So here the rows are read without tracking and deleted through a second context over the same database
    // (see InMemoryContext), which leaves the query's own context as a statement would have left it.
    public static partial class BatchDeleteExtensions
    {
        private static int DeleteInMemory<T>(IQueryable<T> query)
            where T : class
        {
            var entities = query.AsNoTracking().ToList();

            using (var context = InMemoryContext.Create(query.GetInMemoryContext()))
            {
                StageDelete(context, entities);

                context.SaveChanges();

                return entities.Count;
            }
        }

        private static async Task<int> DeleteInMemoryAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
            where T : class
        {
            var entities = await query.AsNoTracking().ToListAsync(cancellationToken);

            using (var context = InMemoryContext.Create(query.GetInMemoryContext()))
            {
                StageDelete(context, entities);

                await context.SaveChangesAsync(cancellationToken);

                return entities.Count;
            }
        }

        private static void StageDelete<T>(InMemoryContext context, IEnumerable<T> entities)
            where T : class
        {
            foreach (var entity in entities)
            {
                context.Attach(entity, EntityState.Deleted);
            }
        }
    }
}