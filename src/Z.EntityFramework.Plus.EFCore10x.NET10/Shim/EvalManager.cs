namespace Z.Expressions
{
    /// <summary>
    /// Stand-in for <c>Z.Expressions.EvalManager</c>. Entity Framework Plus only writes this flag;
    /// nothing in this assembly reads it.
    /// </summary>
    internal static class EvalManager
    {
        public static bool IsCommunity { get; set; }
    }
}