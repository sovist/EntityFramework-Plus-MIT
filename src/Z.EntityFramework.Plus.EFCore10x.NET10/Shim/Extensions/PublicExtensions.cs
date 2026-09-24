using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;

namespace Z.EntityFramework.Extensions
{
    /// <summary>
    /// Stand-in for <c>Z.EntityFramework.Extensions.PublicExtensions</c>.
    /// </summary>
    internal static class PublicExtensions
    {
        /// <summary>
        /// Returns the query itself. The Extensions library unwraps LinqKit's <c>ExpandableQuery&lt;T&gt;</c>
        /// here; this build does not support LinqKit-wrapped queries.
        /// </summary>
        public static IQueryable<T> GetInnerForLinqKit<T>(this IQueryable<T> query)
        {
            return query;
        }

        /// <summary>Non-generic counterpart of <see cref="GetInnerForLinqKit{T}"/>; returns the query itself.</summary>
        public static IQueryable GetInnerForLinqKit(this IQueryable query)
        {
            return query;
        }

        /// <summary>
        /// Returns the name Query Future prefixes when it renames the parameters of combined queries:
        /// the SQL parameter name without its leading '@' when EF Core assigned one that differs from
        /// the invariant name, otherwise the invariant name. This is the logic Entity Framework Plus
        /// used before it delegated to the Extensions library (upstream commit e734b42).
        /// </summary>
        public static string GetParameterName(IRelationalParameter relationalParameter)
        {
            if (relationalParameter is TypeMappedRelationalParameter typeMapped
                && typeMapped.Name != null
                && typeMapped.Name.StartsWith("@_", StringComparison.Ordinal)
                && typeMapped.Name.Substring(1) != relationalParameter.InvariantName)
            {
                return typeMapped.Name.Substring(1);
            }

            return relationalParameter.InvariantName;
        }
    }
}