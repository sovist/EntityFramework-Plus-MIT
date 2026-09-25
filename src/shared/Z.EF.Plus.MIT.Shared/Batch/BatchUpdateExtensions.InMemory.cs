using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Z.EntityFramework.Plus
{
    // The InMemory provider supports neither ExecuteUpdate nor ExecuteDelete - it throws rather than falling back.
    // So here the rows are read without tracking and saved through a second context over the same database
    // (see InMemoryContext), which leaves the query's own context as a statement would have left it.
    public static partial class BatchUpdateExtensions
    {
        private static int UpdateInMemory<T>(IQueryable<T> query, Expression<Func<T, T>> updateFactory)
            where T : class
        {
            var entities = query.AsNoTracking().ToList();

            using (var context = InMemoryContext.Create(query.GetInMemoryContext()))
            {
                StageUpdate(context, entities, updateFactory);

                context.SaveChanges();

                return entities.Count;
            }
        }

        private static async Task<int> UpdateInMemoryAsync<T>(IQueryable<T> query, Expression<Func<T, T>> updateFactory, CancellationToken cancellationToken)
            where T : class
        {
            var entities = await query.AsNoTracking().ToListAsync(cancellationToken);

            using (var context = InMemoryContext.Create(query.GetInMemoryContext()))
            {
                StageUpdate(context, entities, updateFactory);

                await context.SaveChangesAsync(cancellationToken);

                return entities.Count;
            }
        }

        /// <summary>
        /// Attaches each row to the saving context and applies the update factory to it, marking only the
        /// members the factory assigns as modified - the same object initializer the statement path
        /// translates, so both paths set the same members and nothing else.
        /// </summary>
        private static void StageUpdate<T>(DbContext context, ICollection<T> entities, Expression<Func<T, T>> updateFactory)
            where T : class
        {
            if (entities.Count == 0)
            {
                return;
            }

            var factory = updateFactory.Compile();
            var members = Assignments(updateFactory).Select(assignment => assignment.Member).ToList();

            foreach (var entity in entities)
            {
                var updated = factory(entity);

                // Entry(...).State attaches the row alone; Attach and Update would walk its navigations too.
                var entry = context.Entry(entity);
                entry.State = EntityState.Unchanged;

                foreach (var member in members)
                {
                    SetValue(entity, member, GetValue(updated, member));
                    entry.Property(member.Name).IsModified = true;
                }
            }
        }

        private static object GetValue(object entity, MemberInfo member) => member switch
        {
            PropertyInfo property => property.GetValue(entity),
            _ => ((FieldInfo)member).GetValue(entity),
        };

        private static void SetValue(object entity, MemberInfo member, object value)
        {
            if (member is PropertyInfo property)
            {
                property.SetValue(entity, value);
                return;
            }

            ((FieldInfo)member).SetValue(entity, value);
        }
    }
}