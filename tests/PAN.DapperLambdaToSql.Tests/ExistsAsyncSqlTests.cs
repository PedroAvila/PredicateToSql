using System.Data.Common;
using NSubstitute;
using PAN.DapperLambdaToSql.Tests.Fakes;

namespace PAN.DapperLambdaToSql.Tests;

public abstract class ExistsAsyncSqlTests
{
    protected abstract DbConnection CreateConnection();
    protected abstract string D(string identifier);

    [Fact]
    public async Task ExistsAsync_ShouldDelimitTheTableAndTheColumn()
    {
        using var connection = CreateConnection();

        var sql = await SqlCapture.ExistsAsync<User>(connection, u => u.Name == "Pedro");

        Assert.Equal(
            $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {D("Users")} WHERE {D("Name")} = @param0) THEN 1 ELSE 0 END",
            sql);
    }

    [Fact]
    public async Task ExistsAsync_WhenColumnIsAReservedWord_ShouldDelimitIt()
    {
        using var connection = CreateConnection();

        var sql = await SqlCapture.ExistsAsync<Role>(connection, r => r.Key == "ADMIN");

        Assert.Equal(
            $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {D("Roles")} WHERE {D("Key")} = @param0) THEN 1 ELSE 0 END",
            sql);
    }

    [Fact]
    public async Task ExistsAsync_WhenPredicateUsesAndAlso_ShouldDelimitBothColumns()
    {
        using var connection = CreateConnection();

        var sql = await SqlCapture.ExistsAsync<User>(connection, u => u.Name == "Pedro" && u.RoleId == 2);

        Assert.Equal(
            $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {D("Users")} WHERE {D("Name")} = @param0 AND {D("RoleId")} = @param1) THEN 1 ELSE 0 END",
            sql);
    }

    [Fact]
    public async Task ExistsAsync_WhenComparingToACapturedVariable_ShouldParameterizeIt()
    {
        using var connection = CreateConnection();
        var name = "Pedro";

        var sql = await SqlCapture.ExistsAsync<User>(connection, u => u.Name == name);

        Assert.Equal(
            $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {D("Users")} WHERE {D("Name")} = @param0) THEN 1 ELSE 0 END",
            sql);
    }

    [Fact]
    public async Task ExistsAsync_WhenPredicateUsesOrElseGrouping_ShouldDelimitAndParenthesize()
    {
        using var connection = CreateConnection();

        var sql = await SqlCapture.ExistsAsync<User>(connection,
            u => u.Name == "Pedro" && (u.RoleId == 2 || u.RoleId == 3));

        Assert.Equal(
            $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {D("Users")} WHERE {D("Name")} = @param0 AND ({D("RoleId")} = @param1 OR {D("RoleId")} = @param2)) THEN 1 ELSE 0 END",
            sql);
    }
}

public class ExistsAsyncMySqlTests : ExistsAsyncSqlTests
{
    protected override DbConnection CreateConnection() => new MySqlConnection();
    protected override string D(string identifier) => $"`{identifier}`";
}

public class ExistsAsyncSqlServerTests : ExistsAsyncSqlTests
{
    protected override DbConnection CreateConnection() => new SqlConnection();
    protected override string D(string identifier) => $"[{identifier}]";
}

public class DialectCollaborationTests : IDisposable
{
    public void Dispose() => SqlDialects.Forced = null;

    [Fact]
    public async Task UpdateAsync_ShouldDelegateEveryIdentifierToTheDialect()
    {
        var dialect = Substitute.For<ISqlDialect>();
        dialect.Delimit(Arg.Any<string>()).Returns(call => $"<{call.Arg<string>()}>");
        SqlDialects.Forced = dialect;

        using var connection = new MySqlConnection();
        var sql = await SqlCapture.UpdateAsync(connection, new Gym { Id = 1, Name = "Central" });

        Assert.Equal("UPDATE <Gyms> SET <Name> = @Name WHERE <Id> = @Id", sql);

        dialect.Received(1).Delimit("Gyms");
        dialect.Received(1).Delimit("Name");
        dialect.Received(1).Delimit("Id");
    }
}
