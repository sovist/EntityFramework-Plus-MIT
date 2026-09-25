using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Z.EntityFramework.Plus
{
    /// <summary>
    /// Batch Update: updates every row a query matches in one statement, from a factory written as the
    /// entity being rebuilt - <c>query.Update(task =&gt; new Task { Status = 1 })</c>.
    /// </summary>
    /// <remarks>
    /// Upstream Entity Framework Plus implements this on EF Core through Z.EntityFramework.Extensions.
    /// This build translates the factory into EF Core's own <c>ExecuteUpdate</c>, one <c>SetProperty</c>
    /// call per assigned member, so the database still does the work in one statement without loading a
    /// row - under EF Core's translation rules rather than Z.EntityFramework.Extensions' SQL generation.
    /// <para>
    /// Provided: the object-initializer factory, <c>Update</c> and <c>UpdateAsync</c>.
    /// Not provided: the <c>ExpandoObject</c>, <c>IDictionary</c> and anonymous-object forms, and the <c>BatchUpdate</c> options builder.
    /// </para>
    /// </remarks>
    public static partial class BatchUpdateExtensions
    {
        /// <summary>
        /// Updates the rows the query matches, setting the members the factory assigns.
        /// </summary>
        /// <returns>The number of rows updated.</returns>
        public static int Update<T>(this IQueryable<T> query, Expression<Func<T, T>> updateFactory)
            where T : class
        {
            if (query.IsInMemoryQueryContext())
            {
                return UpdateInMemory(query, updateFactory);
            }

            return query.ExecuteUpdate(setters => ApplySetters(setters, updateFactory));
        }

        /// <summary>
        /// Updates the rows the query matches, setting the members the factory assigns.
        /// </summary>
        /// <returns>The number of rows updated.</returns>
        public static Task<int> UpdateAsync<T>(this IQueryable<T> query, Expression<Func<T, T>> updateFactory, CancellationToken cancellationToken = default)
            where T : class
        {
            if (query.IsInMemoryQueryContext())
            {
                return UpdateInMemoryAsync(query, updateFactory, cancellationToken);
            }

            return query.ExecuteUpdateAsync(setters => ApplySetters(setters, updateFactory), cancellationToken);
        }

        /// <summary>
        /// Turns each member the factory assigns into one <c>SetProperty</c> call. Both halves are lambdas
        /// over the entity, so a value may read the row it updates - <c>task =&gt; new Task { Order = task.Order + 1 }</c>
        /// translates as readily as a constant.
        /// </summary>
        private static void ApplySetters<T>(UpdateSettersBuilder<T> setters, Expression<Func<T, T>> updateFactory)
        {
            var entity = updateFactory.Parameters[0];

            foreach (var assignment in Assignments(updateFactory))
            {
                var memberType = MemberType(assignment.Member);
                var property = Expression.Lambda(Expression.MakeMemberAccess(entity, assignment.Member), entity);

                if (ReadsRow(assignment.Expression, entity))
                {
                    SetPropertyToExpression<T>(memberType).Invoke(setters, [property, Expression.Lambda(assignment.Expression, entity)]);
                    continue;
                }

                // A value that does not read the row - a constant or a captured local - goes through the
                // value overload, which EF Core parameterises. The expression overload would also work,
                // except on EF Core 10.0.0 - 10.0.6, where a constant assigned to a nullable member fails
                // to translate ("No coercion operator is defined", dotnet/efcore#37974).
                SetPropertyToValue<T>(memberType).Invoke(setters, [property, Evaluate(assignment.Expression, memberType)]);
            }
        }

        private static bool ReadsRow(Expression value, ParameterExpression entity) => new ParameterFinder(entity).Found(value);

        private sealed class ParameterFinder(ParameterExpression parameter) : ExpressionVisitor
        {
            private bool _found;

            public bool Found(Expression expression)
            {
                Visit(expression);
                return _found;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                _found |= node == parameter;
                return node;
            }
        }

        private static object Evaluate(Expression value, Type type)
        {
            if (value is ConstantExpression constant && constant.Type == type)
            {
                return constant.Value;
            }

            var convert = Expression.Convert(value, typeof(object));

            var lambda = Expression.Lambda<Func<object>>(convert);

            return lambda.Compile()();
        }

        /// <summary>
        /// Reads the members an update factory assigns.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The factory is not an object initializer. Every other shape - a constructor call, a conditional,
        /// a method returning an entity - names no members to set, so it cannot be translated at all.
        /// </exception>
        private static IReadOnlyList<MemberAssignment> Assignments<T>(Expression<Func<T, T>> updateFactory)
        {
            if (updateFactory.Body is not MemberInitExpression initializer)
            {
                throw new ArgumentException($"The update factory has to be an object initializer, as in " +
                                            $"`entity => new {typeof(T).Name} {{ Property = value }}`, but it is {updateFactory.Body.NodeType}.", nameof(updateFactory));
            }

            return initializer.Bindings
                .Select(binding => binding as MemberAssignment ?? throw new ArgumentException($"The update factory assigns [{binding.Member.Name}] by {binding.BindingType}, which names no value to set it to.", nameof(updateFactory)))
                .ToList();
        }

        // One reflected SetProperty per member type and overload; these run inside request handling.
        private static readonly ConcurrentDictionary<(Type Entity, Type Member, bool ToExpression), MethodInfo> SetPropertyMethods = new();

        private static MethodInfo SetPropertyToExpression<T>(Type memberType) => SetProperty<T>(memberType, toExpression: true);

        private static MethodInfo SetPropertyToValue<T>(Type memberType) => SetProperty<T>(memberType, toExpression: false);

        private static MethodInfo SetProperty<T>(Type memberType, bool toExpression) => SetPropertyMethods.GetOrAdd(
            (typeof(T), memberType, toExpression),
            key => typeof(UpdateSettersBuilder<>)
                .MakeGenericType(key.Entity)
                .GetMethods()
                .Single(method => method.Name == nameof(UpdateSettersBuilder<object>.SetProperty) &&
                                  method.IsGenericMethodDefinition &&
                                  method.GetParameters() is { Length: 2 } parameters &&
                                  IsExpression(parameters[0].ParameterType) &&
                                  IsExpression(parameters[1].ParameterType) == key.ToExpression)
                .MakeGenericMethod(key.Member));

        private static bool IsExpression(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Expression<>);

        private static Type MemberType(MemberInfo member) => member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,

            _ => throw new ArgumentException($"[{member.Name}] is neither a property nor a field.", nameof(member)),
        };
    }
}