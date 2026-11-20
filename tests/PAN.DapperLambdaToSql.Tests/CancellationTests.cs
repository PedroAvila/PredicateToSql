using PAN.DapperLambdaToSql.Tests.Fakes;

namespace PAN.DapperLambdaToSql.Tests;

public class CancellationTests
{
    [Fact]
    public async Task UpdateAsync_WhenTokenIsPassed_ShouldPropagateItToDapper()
    {
        using var connection = new SqlConnection();
        using var cts = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<SqlCapturedException>(
            () => connection.UpdateAsync(new Gym { Id = 1, Name = "Central" }, cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task ExistsAsync_WhenTokenIsPassed_ShouldPropagateItToDapper()
    {
        using var connection = new SqlConnection();
        using var cts = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<SqlCapturedException>(
            () => connection.ExistsAsync<Gym>(g => g.Name == "Central", cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task QueryAsync_WhenTokenIsPassed_ShouldPropagateItToDapper()
    {
        using var connection = new SqlConnection();
        using var cts = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<SqlCapturedException>(
            () => connection.QueryAsync<Gym>(g => g.Name == "Central", cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
    }
}
