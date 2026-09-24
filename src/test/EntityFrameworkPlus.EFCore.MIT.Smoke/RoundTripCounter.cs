using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkPlus.EFCore.MIT.Smoke;

/// <summary>Reads the server round-trip count SqlClient keeps for one open <see cref="SqlConnection"/>.</summary>
public sealed class RoundTripCounter
{
    private readonly SqlConnection _connection;

    public RoundTripCounter(SqlConnection connection)
    {
        _connection = connection;
    }

    public long RoundTrips => (long)_connection.RetrieveStatistics()["ServerRoundtrips"];
}

public static class RoundTripCounterExtensions
{
    /// <summary>
    /// Opens the context's connection and starts counting server round trips on it. Keeping the connection
    /// open makes EF Core and Query Future share the one <see cref="SqlConnection"/> whose statistics are read.
    /// </summary>
    public static RoundTripCounter OpenWithRoundTripCounter(this DbContext context)
    {
        context.Database.OpenConnection();
        var connection = (SqlConnection)context.Database.GetDbConnection();
        connection.StatisticsEnabled = true;
        connection.ResetStatistics();
        return new RoundTripCounter(connection);
    }
}