// The compile step below mirrors EF Core's QueryCompiler.ExecuteCore and QueryCompilationContext.CreateQueryExecutorExpression
// (github.com/dotnet/efcore, release/10.0). EF Core is licensed under the MIT License, Copyright (c) .NET Foundation and Contributors.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;

namespace Z.EntityFramework.Extensions
{
    /// <summary>
    /// Stand-in for the Z.EntityFramework.Extensions methods that turn a LINQ query into the three things Query
    /// Future and Query Cache work with: the relational command (SQL text and parameters), the query context that
    /// holds the parameter values, and EF Core's not-yet-executed enumerable that will materialise the results.
    /// </summary>
    /// <remarks>
    /// This is EF Core's own <c>QueryCompilationContext.CreateQueryExecutorExpression</c> pipeline, run step by
    /// step with the public factories, with one change: the translated query's result cardinality is forced to
    /// <see cref="ResultCardinality.Enumerable"/>. EF Core compiles a scalar query such as <c>Count()</c> to a
    /// delegate returning the value itself; Query Future needs an enumerable it can drive against its own reader
    /// (and whose <c>_readerColumns</c> it can clear so a buffered reader never swallows the next result set).
    /// With the override, <c>SELECT COUNT(*) …</c> becomes a one-row enumerable with identical SQL.
    /// </remarks>
    internal static class QueryCommandExtensions
    {
        private static readonly FieldInfo QueryCompilerField = GetRequiredField(typeof(EntityQueryProvider), "_queryCompiler");

        private static readonly FieldInfo RuntimeParametersField = GetRequiredField(typeof(QueryCompilationContext), "_runtimeParameters");

        private static readonly PropertyInfo QueryContextParametersProperty = typeof(QueryContext).GetProperty(nameof(QueryContext.Parameters));

        private static readonly MethodInfo ParameterDictionaryAddMethod = typeof(Dictionary<string, object>).GetMethod(nameof(Dictionary<string, object>.Add));

        private static readonly MethodInfo CreateExecutorMethod = typeof(QueryCommandExtensions).GetMethod(nameof(CreateExecutor), BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly ConcurrentDictionary<Type, FieldInfo> CommandResolverFields = new ConcurrentDictionary<Type, FieldInfo>();

        /// <summary>
        /// Compiled executors keyed by EF Core's own compiled-query cache key plus the element type. EF Core's
        /// <c>ICompiledQueryCache</c> cannot be shared: the same key would map to a scalar delegate there and to
        /// an enumerable delegate here, and whichever was cached first would be returned for both.
        /// </summary>
        private static readonly MemoryCache ExecutorCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10240 });

        /// <summary>
        /// Compiles the query and returns its relational command, together with the query context that holds
        /// the extracted parameter values. Nothing is executed.
        /// </summary>
        public static IRelationalCommand CreateCommand(this IQueryable query, out RelationalQueryContext queryContext)
        {
            return query.EFPlusCreateCommand(null, out queryContext, out _);
        }

        /// <summary>
        /// Compiles the query and returns its relational command, the query context that holds the extracted
        /// parameter values, and EF Core's not-yet-executed enumerable for the query. Nothing is executed.
        /// </summary>
        /// <param name="query">The query; its provider must be EF Core's <see cref="EntityQueryProvider"/>.</param>
        /// <param name="beforeCompile">
        /// Invoked with the query before anything else happens. Query Future uses it to swap the connection EF
        /// Core will read from, so the enumerable returned in <paramref name="compiledQuery"/> later reads its
        /// result set from the combined command instead of opening one of its own.
        /// </param>
        /// <param name="queryContext">The query context the enumerable is bound to, with parameter values populated.</param>
        /// <param name="compiledQuery">
        /// An <see cref="IEnumerable{T}"/> for the query's element type, bound to <paramref name="queryContext"/>.
        /// Enumerating it executes the query.
        /// </param>
        public static IRelationalCommand EFPlusCreateCommand(
            this IQueryable query,
            Action<IQueryable> beforeCompile,
            out RelationalQueryContext queryContext,
            out object compiledQuery)
        {
            var queryCompiler = (QueryCompiler)QueryCompilerField.GetValue(query.Provider);

            beforeCompile?.Invoke(query);

            var queryContextFactory = (IQueryContextFactory)PublicMethods.GetQueryContextFactory(queryCompiler);
            queryContext = (RelationalQueryContext)queryContextFactory.Create();

            var context = queryContext.Context;
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var expression = queryCompiler.ExtractParameters(query.Expression, queryContext.Parameters, logger, compiledQuery: false, generateContextAccessors: false);

            var executor = GetOrCreateExecutor(context, expression, query.ElementType);
            var enumerable = executor(queryContext);
            compiledQuery = enumerable;

            var resolverField = CommandResolverFields.GetOrAdd(enumerable.GetType(), type => GetRequiredField(type, "_relationalCommandResolver"));
            var resolver = (RelationalCommandResolver)resolverField.GetValue(enumerable);

            return (IRelationalCommand)resolver(queryContext.Parameters);
        }

        private static Func<QueryContext, object> GetOrCreateExecutor(DbContext context, Expression expression, Type elementType)
        {
            var cacheKey = (context.GetService<ICompiledQueryCacheKeyGenerator>().GenerateCacheKey(expression, async: false), elementType);

            return ExecutorCache.GetOrCreate(cacheKey, entry =>
            {
                entry.SetSize(1);
                return (Func<QueryContext, object>)CreateExecutorMethod.MakeGenericMethod(elementType).Invoke(null, new object[] { context, expression });
            });
        }

        /// <summary>
        /// <c>QueryCompilationContext.CreateQueryExecutor&lt;IEnumerable&lt;T&gt;&gt;</c>, with the result
        /// cardinality forced to <see cref="ResultCardinality.Enumerable"/> after translation.
        /// </summary>
        private static Func<QueryContext, object> CreateExecutor<T>(DbContext context, Expression expression)
        {
            var compilationContext = context.GetService<IQueryCompilationContextFactory>().Create(async: false);
            var logger = compilationContext.Logger;
            var expressionPrinter = new ExpressionPrinter();

            var query = logger.QueryCompilationStarting(context, expressionPrinter, expression).Query;
            query = context.GetService<IQueryTranslationPreprocessorFactory>().Create(compilationContext).Process(query);
            query = context.GetService<IQueryableMethodTranslatingExpressionVisitorFactory>().Create(compilationContext).Translate(query);

            if (query is ShapedQueryExpression shapedQuery && shapedQuery.ResultCardinality != ResultCardinality.Enumerable)
            {
                query = shapedQuery.UpdateResultCardinality(ResultCardinality.Enumerable);
            }

            query = context.GetService<IQueryTranslationPostprocessorFactory>().Create(compilationContext).Process(query);
            query = context.GetService<IShapedQueryCompilingExpressionVisitorFactory>().Create(compilationContext).Visit(query);
            query = InsertRuntimeParameters(compilationContext, query);

            var executorExpression = Expression.Lambda<Func<QueryContext, IEnumerable<T>>>(query, QueryCompilationContext.QueryContextParameter);
            #pragma warning disable EF9100 // [Experimental] precompiled-query members; EF Core itself calls both in QueryCompilationContext.CreateQueryExecutor.
            var liftedExpression = (Expression<Func<QueryContext, IEnumerable<T>>>)context.GetService<ILiftableConstantProcessor>()
                .InlineConstants(executorExpression, compilationContext.SupportsPrecompiledQuery);
            #pragma warning restore EF9100

            Func<QueryContext, IEnumerable<T>> executor;
            try
            {
                executor = liftedExpression.Compile();
            }
            finally
            {
                logger.QueryExecutionPlanned(context, expressionPrinter, executorExpression);
            }

            return queryContext => executor(queryContext);
        }

        /// <summary>
        /// <c>QueryCompilationContext.InsertRuntimeParameters</c>: prepends, to the compiled query, the calls
        /// that add each runtime parameter (global query filters that read context members, for instance) to
        /// the query context's parameter dictionary.
        /// </summary>
        private static Expression InsertRuntimeParameters(QueryCompilationContext compilationContext, Expression query)
        {
            var runtimeParameters = (Dictionary<string, LambdaExpression>)RuntimeParametersField.GetValue(compilationContext);
            if (runtimeParameters == null)
            {
                return query;
            }

            return Expression.Block(
                runtimeParameters
                    .Select(parameter => (Expression)Expression.Call(
                        Expression.Property(QueryCompilationContext.QueryContextParameter, QueryContextParametersProperty),
                        ParameterDictionaryAddMethod,
                        Expression.Constant(parameter.Key),
                        Expression.Convert(Expression.Invoke(parameter.Value, QueryCompilationContext.QueryContextParameter), typeof(object))))
                    .Append(query));
        }

        private static FieldInfo GetRequiredField(Type type, string name)
        {
            return type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                   ?? throw new MissingFieldException(type.FullName, name);
        }
    }
}