using PAN.PredicateToSql.Tests.Fakes;

namespace PAN.PredicateToSql.Tests;

public class DialectDetectionTests : IDisposable
{
    public void Dispose() => SqlDialects.Forced = null;

    [Fact]
    public void GetDialect_WhenConnectionIsSqlConnection_ShouldReturnSqlServer()
    {
        using var connection = new SqlConnection();
        Assert.Same(SqlDialects.SqlServer, DapperHelper.GetDialect(connection));
    }

    [Fact]
    public void GetDialect_WhenConnectionIsMySqlConnection_ShouldReturnMySql()
    {
        using var connection = new MySqlConnection();
        Assert.Same(SqlDialects.MySql, DapperHelper.GetDialect(connection));
    }

    [Fact]
    public void GetDialect_WhenProviderIsUnknown_ShouldFallBackToNoDelimiter()
    {
        // Un proveedor no reconocido conserva el comportamiento previo a multi-motor
        // en lugar de arriesgar los delimitadores del motor equivocado.
        using var connection = new NpgsqlConnection();
        Assert.Same(SqlDialects.None, DapperHelper.GetDialect(connection));
    }

    [Fact]
    public void GetDialect_WhenConnectionIsNull_ShouldFallBackToNoDelimiter()
        => Assert.Same(SqlDialects.None, DapperHelper.GetDialect(null!));

    [Fact]
    public void GetDialect_WhenDialectIsForced_ShouldWinOverDetection()
    {
        SqlDialects.Forced = SqlDialects.SqlServer;

        using var connection = new MySqlConnection();
        Assert.Same(SqlDialects.SqlServer, DapperHelper.GetDialect(connection));
    }

    [Fact]
    public async Task UpdateAsync_WhenDialectIsForced_ShouldUseItInsteadOfTheConnection()
    {
        SqlDialects.Forced = SqlDialects.SqlServer;

        using var connection = new MySqlConnection();
        var sql = await SqlCapture.UpdateAsync(connection, new Gym { Id = 1, Name = "Central" });

        Assert.Equal("UPDATE [Gyms] SET [Name] = @Name WHERE [Id] = @Id", sql);
    }
}
