namespace EntityFrameworkPlus.EFCore.MIT.Smoke.Batch
{
    /// <summary>
    /// <c>BatchUpdateExtensions</c>: the object-initializer factory translated to <c>ExecuteUpdate</c>, and the InMemory fallback, on the same assertions.
    /// </summary>
    public partial class BatchUpdateExtensionsTests : BatchExtensionsTestsBase
    {
        private static readonly DateTime DeleteAfter = new(2026, 9, 24, 12, 0, 0);

        private const int ArchiveBufferId = 99;
    }
}