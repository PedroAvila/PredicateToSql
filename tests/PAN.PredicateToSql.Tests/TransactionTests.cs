using PAN.PredicateToSql.Tests.Fakes;

namespace PAN.PredicateToSql.Tests;

public class TransactionTests
{
    [Fact]
    public async Task UpdateAsync_WhenTransactionIsPassed_ShouldPropagateItToDapper()
    {
        using var connection = new SqlConnection();
        using var transaction = new CapturingTransaction();

        var exception = await Assert.ThrowsAsync<SqlCapturedException>(
            () => connection.UpdateAsync(
                new Gym { Id = 1, Name = "Central" }, transaction: transaction));

        Assert.Same(transaction, exception.Transaction);
    }

    [Fact]
    public async Task ExistsAsync_WhenTransactionIsPassed_ShouldPropagateItToDapper()
    {
        using var connection = new SqlConnection();
        using var transaction = new CapturingTransaction();

        var exception = await Assert.ThrowsAsync<SqlCapturedException>(
            () => connection.ExistsAsync<Gym>(g => g.Name == "Central", transaction: transaction));

        Assert.Same(transaction, exception.Transaction);
    }

    [Fact]
    public async Task QueryAsync_WhenTransactionIsPassed_ShouldPropagateItToDapper()
    {
        using var connection = new SqlConnection();
        using var transaction = new CapturingTransaction();

        var exception = await Assert.ThrowsAsync<SqlCapturedException>(
            () => connection.QueryAsync<Gym>(g => g.Name == "Central", transaction: transaction));

        Assert.Same(transaction, exception.Transaction);
    }
}
