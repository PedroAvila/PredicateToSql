using System.Data;
using PAN.DapperLambdaToSql.Tests.Fakes;

namespace PAN.DapperLambdaToSql.Tests;

public class NullGuardTests
{
    [Fact]
    public async Task UpdateAsync_WhenConnectionIsNull_ShouldThrowArgumentNullException()
    {
        IDbConnection? connection = null;

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => connection!.UpdateAsync(new Gym { Id = 1, Name = "Central" }));

        Assert.Equal("connection", exception.ParamName);
    }

    [Fact]
    public async Task ExistsAsync_WhenConnectionIsNull_ShouldThrowArgumentNullException()
    {
        IDbConnection? connection = null;

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => connection!.ExistsAsync<Gym>(g => g.Id == 1));

        Assert.Equal("connection", exception.ParamName);
    }

    [Fact]
    public async Task QueryAsync_WhenConnectionIsNull_ShouldThrowArgumentNullException()
    {
        IDbConnection? connection = null;

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => connection!.QueryAsync<Gym>(g => g.Id == 1));

        Assert.Equal("connection", exception.ParamName);
    }

    [Fact]
    public async Task UpdateAsync_WhenEntityIsNull_ShouldThrowArgumentNullException()
    {
        using var connection = new SqlConnection();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => connection.UpdateAsync<User>(null!));

        Assert.Equal("entity", exception.ParamName);
    }

    [Fact]
    public async Task ExistsAsync_WhenPredicateIsNull_ShouldThrowArgumentNullException()
    {
        using var connection = new SqlConnection();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => connection.ExistsAsync<User>(null!));

        Assert.Equal("predicate", exception.ParamName);
    }

    [Fact]
    public async Task QueryAsync_WhenPredicateIsNull_ShouldThrowArgumentNullException()
    {
        using var connection = new SqlConnection();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => connection.QueryAsync<User>(null!));

        Assert.Equal("predicate", exception.ParamName);
    }
}
