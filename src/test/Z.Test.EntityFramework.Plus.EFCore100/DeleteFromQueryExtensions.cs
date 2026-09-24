using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Z.Test.EntityFramework.Plus
{
    /// <summary>
    /// Stand-in for Z.EntityFramework.Extensions' <c>DeleteFromQuery</c>, which a few upstream tests use to clear tables between runs.
    /// EF Core's own bulk delete does that job here.
    /// </summary>
    internal static class DeleteFromQueryExtensions
    {
        public static int DeleteFromQuery<T>(this IQueryable<T> query)
        {
            return query.ExecuteDelete();
        }
    }
}