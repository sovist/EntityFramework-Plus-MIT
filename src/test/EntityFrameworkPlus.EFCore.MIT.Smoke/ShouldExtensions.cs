namespace EntityFrameworkPlus.EFCore.MIT.Smoke
{
    /// <summary>
    /// Sequence assertions that read as "these elements, in this order": the selector sits next to the
    /// values it is checked against, and a failure names the first index that differs.
    /// </summary>
    public static class ShouldExtensions
    {
        public static void ShouldBeInOrder<T, TProperty>(this IEnumerable<T> actual, Func<T, TProperty> propertySelector, params TProperty[] expected)
        {
            actual.ShouldNotBeNull();

            ShouldBeInOrder(actual.Select(propertySelector).ToList(), expected);
        }

        public static void ShouldBeInOrder<T>(this IEnumerable<T> actual, params T[] expected)
        {
            actual.ShouldNotBeNull();

            var actualOrder = actual.ToList();

            actualOrder.Count.ShouldBe(expected.Length);

            for (var i = 0; i < actualOrder.Count; i++)
            {
                actualOrder[i].ShouldBe(expected[i], $"At index [{i}], type [{typeof(T)}]");
            }
        }
    }
}