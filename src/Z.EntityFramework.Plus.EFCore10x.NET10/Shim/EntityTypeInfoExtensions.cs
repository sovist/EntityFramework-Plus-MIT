using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Z.EntityFramework.Plus
{
    /// <summary>
    /// Stand-in for the Z.EntityFramework.Extensions entity metadata helper Set Identity uses to build the
    /// table name for <c>SET IDENTITY_INSERT</c>. Lives in this namespace because the caller imports no other.
    /// </summary>
    internal static class EntityTypeInfoExtensions
    {
        public static EntityTypeInfo ToZInfo(this IEntityType entityType)
        {
            return new EntityTypeInfo(entityType.GetSchema(), entityType.GetTableName());
        }
    }

    /// <summary>The schema and table an entity type is mapped to, as EF Core reports them.</summary>
    internal sealed class EntityTypeInfo
    {
        public EntityTypeInfo(string schemaNameEF, string tableNameEF)
        {
            SchemaNameEF = schemaNameEF;
            TableNameEF = tableNameEF;
        }

        public string SchemaNameEF { get; }

        public string TableNameEF { get; }
    }
}