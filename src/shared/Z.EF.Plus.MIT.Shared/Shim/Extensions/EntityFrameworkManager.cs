using System;
using Microsoft.EntityFrameworkCore;

namespace Z.EntityFramework.Extensions
{
    /// <summary>
    /// Stand-in for <c>Z.EntityFramework.Extensions.EntityFrameworkManager</c>: the flags Entity Framework Plus
    /// writes to tell the Extensions library it is present (nothing in this assembly reads them), and the one
    /// hook the Batch Update / Delete fallback for the InMemory provider can take.
    /// </summary>
    public static class EntityFrameworkManager
    {
        public static bool IsEntityFrameworkPlus { get; set; }

        public static bool IsCommunity { get; set; }

        /// <summary>
        /// Creates a second <see cref="DbContext"/> over the same InMemory database as the given one,
        /// for Batch Update / Delete to save through on the InMemory provider, so the query's own context stays as a statement would leave it.
        /// Optional: when unset, or when it returns null, the fallback constructs the context's own type from its options.
        /// Set it for a context whose constructor needs more than its options - one resolved from a container, for instance.
        /// The context it returns stays its owner's: the fallback never disposes it, may be handed the same instance on
        /// every call, and detaches the rows it attached before handing it back.
        /// </summary>
        public static Func<DbContext, DbContext> ContextFactory { get; set; }
    }
}