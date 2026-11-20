using System.Reflection;

namespace PAN.PredicateToSql.Tests;

public class KeyResolutionTests
{
    private static PropertyInfo ResolveKey<T>()
    {
        var type = typeof(T);
        return DapperHelper.GetKeyProperty(type, type.GetProperties(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void GetKeyProperty_WhenEntityHasId_ShouldResolveIt()
        => Assert.Equal("Id", ResolveKey<Role>().Name);

    [Fact]
    public void GetKeyProperty_WhenIdIsUpperCase_ShouldResolveItIgnoringCase()
        => Assert.Equal("ID", ResolveKey<Invoice>().Name);

    [Fact]
    public void GetKeyProperty_WhenNamedAfterTheClass_ShouldResolveIt()
        => Assert.Equal("WarehouseId", ResolveKey<Warehouse>().Name);

    [Fact]
    public void GetKeyProperty_WhenExplicitKeyCompetesWithId_ShouldPreferTheAttribute()
        => Assert.Equal("Sku", ResolveKey<Product>().Name);

    [Fact]
    public void GetKeyProperty_WhenKeyAttributeIsPresent_ShouldResolveIt()
        => Assert.Equal("Folio", ResolveKey<Ticket>().Name);

    [Fact]
    public void GetKeyProperty_WhenThereIsNoKey_ShouldThrowWithADescriptiveMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ResolveKey<Detail>());

        Assert.Contains("Detail", exception.Message);
        Assert.Contains("DetailId", exception.Message);
    }

    [Fact]
    public void GetKeyProperty_WhenThereIsNoKey_ShouldNotMistakeAForeignKeyForThePrimaryOne()
    {
        // Detail tiene GymId y CustomerId. La convención busca el nombre concreto
        // "DetailId", nunca un patrón "termina en Id", así que ninguna califica.
        var exception = Assert.Throws<InvalidOperationException>(() => ResolveKey<Detail>());

        Assert.DoesNotContain("GymId", exception.Message);
        Assert.DoesNotContain("CustomerId", exception.Message);
    }

    [Fact]
    public void GetKeyProperty_WhenEntityHasForeignKeys_ShouldStillResolveIdAsThePrimaryKey()
    {
        // User tiene GymId y RoleId; ninguna debe desplazar a Id.
        Assert.Equal("Id", ResolveKey<User>().Name);
    }

    [Fact]
    public void GetKeyProperty_WhenThereAreMultipleKeys_ShouldThrowNotSupported()
    {
        var type = typeof(CompositeKey);

        var exception = Assert.Throws<NotSupportedException>(() =>
            DapperHelper.GetKeyProperty(type, type.GetProperties(BindingFlags.Public | BindingFlags.Instance)));

        Assert.Contains("CompositeKey", exception.Message);
        Assert.Contains("TenantId", exception.Message);
        Assert.Contains("UserId", exception.Message);
    }
}
