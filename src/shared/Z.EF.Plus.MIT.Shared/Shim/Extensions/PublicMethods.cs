using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace Z.EntityFramework.Extensions
{
    /// <summary>
    /// Stand-in for <c>Z.EntityFramework.Extensions.PublicMethods</c>: exposes the two private
    /// <see cref="QueryCompiler"/> fields Entity Framework Plus needs to build and execute combined
    /// commands. This is the inline reflection Entity Framework Plus used before it delegated to the
    /// Extensions library (upstream commit 48e460d).
    /// </summary>
    internal static class PublicMethods
    {
        private static readonly FieldInfo QueryContextFactoryField = GetRequiredField("_queryContextFactory");

        private static readonly FieldInfo DatabaseField = GetRequiredField("_database");

        /// <summary>
        /// Returns the <c>IQueryContextFactory</c> held by an EF Core <see cref="QueryCompiler"/>.
        /// </summary>
        /// <param name="queryCompiler">A <see cref="QueryCompiler"/>; typed as object because callers obtain it by reflection.</param>
        public static object GetQueryContextFactory(object queryCompiler)
        {
            return QueryContextFactoryField.GetValue(queryCompiler);
        }

        /// <summary>
        /// Returns the <c>IDatabase</c> held by an EF Core <see cref="QueryCompiler"/>.
        /// </summary>
        /// <param name="queryCompiler">A <see cref="QueryCompiler"/>; typed as object because callers obtain it by reflection.</param>
        public static object GetDatabase(object queryCompiler)
        {
            return DatabaseField.GetValue(queryCompiler);
        }

        private static FieldInfo GetRequiredField(string name)
        {
            return typeof(QueryCompiler).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                   ?? throw new MissingFieldException(typeof(QueryCompiler).FullName, name);
        }
    }
}