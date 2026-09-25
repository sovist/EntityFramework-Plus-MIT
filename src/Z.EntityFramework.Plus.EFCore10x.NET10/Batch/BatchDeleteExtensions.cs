using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Z.EntityFramework.Plus
{
    /// <summary>
    /// Batch Delete: deletes every row a query matches in one statement - <c>query.Delete()</c>.
    /// </summary>
    /// <remarks>
    /// Upstream Entity Framework Plus implements this on EF Core through Z.EntityFramework.Extensions.
    /// This build uses EF Core's own <c>ExecuteDelete</c>, under EF Core's translation rules.
    /// Provided: <c>Delete</c> and <c>DeleteAsync</c>.
    /// not provided: the <c>BatchDelete</c> options builder.
    /// </remarks>
    public static partial class BatchDeleteExtensions
    {
        /// <summary>
        /// Deletes the rows the query matches.
        /// </summary>
        /// <returns>The number of rows deleted.</returns>
        public static int Delete<T>(this IQueryable<T> query)
            where T : class
        {
            if (query.IsInMemoryQueryContext())
            {
                return DeleteInMemory(query);
            }

            return query.ExecuteDelete();
        }

        /// <summary>
        /// Deletes the rows the query matches.
        /// </summary>
        /// <returns>The number of rows deleted.</returns>
        public static Task<int> DeleteAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
            where T : class
        {
            if (query.IsInMemoryQueryContext())
            {
                return DeleteInMemoryAsync(query, cancellationToken);
            }

            return query.ExecuteDeleteAsync(cancellationToken);
        }
    }
}