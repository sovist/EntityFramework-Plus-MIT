namespace Z.EntityFramework.Extensions
{
    /// <summary>
    /// Stand-in for <c>Z.EntityFramework.Extensions.EntityFrameworkManager</c>.
    /// Entity Framework Plus only ever writes these flags, to tell the Extensions library it is present.
    /// nothing in this assembly reads them.
    /// </summary>
    internal static class EntityFrameworkManager
    {
        public static bool IsEntityFrameworkPlus { get; set; }

        public static bool IsCommunity { get; set; }
    }
}