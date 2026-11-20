namespace PAN.DapperLambdaToSql.Tests;

public class ExpressionTranslationTests
{
    [Fact]
    public void ToSql_WhenComparingToAConstant_ShouldParameterizeTheValue()
    {
        var (sql, parameters) = DapperHelper.ToSql<User>(u => u.Name == "Pedro");

        Assert.Equal("Name = @param0", sql);
        Assert.Equal("Pedro", parameters.Get<string>("@param0"));
    }

    [Fact]
    public void ToSql_WhenComparingToACapturedVariable_ShouldParameterizeItsValue()
    {
        var expected = "pedro@example.com";

        var (sql, parameters) = DapperHelper.ToSql<User>(u => u.Name == expected);

        Assert.Equal("Name = @param0", sql);
        Assert.Equal(expected, parameters.Get<string>("@param0"));
    }

    [Fact]
    public void ToSql_WhenPredicateUsesAndAlso_ShouldJoinBothSides()
    {
        var (sql, parameters) = DapperHelper.ToSql<User>(u => u.Name == "Pedro" && u.RoleId == 2);

        Assert.Equal("Name = @param0 AND RoleId = @param1", sql);
        Assert.Equal("Pedro", parameters.Get<string>("@param0"));
        Assert.Equal(2, parameters.Get<int>("@param1"));
    }

    [Fact]
    public void ToSql_WithSqlServerDialect_ShouldDelimitTheColumn()
    {
        var (sql, _) = DapperHelper.ToSql<Role>(r => r.Key == "ADMIN", SqlDialects.SqlServer);

        Assert.Equal("[Key] = @param0", sql);
    }

    [Fact]
    public void ToSql_WithMySqlDialect_ShouldDelimitTheColumn()
    {
        var (sql, _) = DapperHelper.ToSql<Role>(r => r.Key == "ADMIN", SqlDialects.MySql);

        Assert.Equal("`Key` = @param0", sql);
    }

    [Fact]
    public void ToSql_WithoutDialect_ShouldKeepTheColumnUndelimited()
    {
        var (sql, _) = DapperHelper.ToSql<Role>(r => r.Key == "ADMIN");

        Assert.Equal("Key = @param0", sql);
    }

    [Theory]
    [InlineData("mayor")]
    [InlineData("mayorigual")]
    [InlineData("menor")]
    [InlineData("menorigual")]
    [InlineData("distinto")]
    public void ToSql_WhenUsingAComparisonOperator_ShouldTranslateIt(string caso)
    {
        var (sql, expectedOperator) = caso switch
        {
            "mayor" => (DapperHelper.ToSql<User>(u => u.RoleId > 2).Sql, ">"),
            "mayorigual" => (DapperHelper.ToSql<User>(u => u.RoleId >= 2).Sql, ">="),
            "menor" => (DapperHelper.ToSql<User>(u => u.RoleId < 2).Sql, "<"),
            "menorigual" => (DapperHelper.ToSql<User>(u => u.RoleId <= 2).Sql, "<="),
            _ => (DapperHelper.ToSql<User>(u => u.RoleId != 2).Sql, "<>"),
        };

        Assert.Equal($"RoleId {expectedOperator} @param0", sql);
    }

    [Fact]
    public void ToSql_WhenPredicateUsesOrElse_ShouldJoinBothSidesWithOr()
    {
        var (sql, parameters) = DapperHelper.ToSql<User>(u => u.RoleId == 2 || u.RoleId == 3);

        Assert.Equal("RoleId = @param0 OR RoleId = @param1", sql);
        Assert.Equal(2, parameters.Get<int>("@param0"));
        Assert.Equal(3, parameters.Get<int>("@param1"));
    }

    [Fact]
    public void ToSql_WhenAndAlsoWrapsOrElseOnTheRight_ShouldParenthesizeTheOrElse()
    {
        var (sql, _) = DapperHelper.ToSql<User>(u => u.Name == "Pedro" && (u.RoleId == 2 || u.RoleId == 3));

        Assert.Equal("Name = @param0 AND (RoleId = @param1 OR RoleId = @param2)", sql);
    }

    [Fact]
    public void ToSql_WhenAndAlsoWrapsOrElseOnTheLeft_ShouldParenthesizeTheOrElse()
    {
        var (sql, _) = DapperHelper.ToSql<User>(u => (u.RoleId == 2 || u.RoleId == 3) && u.Name == "Pedro");

        Assert.Equal("(RoleId = @param0 OR RoleId = @param1) AND Name = @param2", sql);
    }

    [Fact]
    public void ToSql_WhenOrElseWrapsAndAlso_ShouldNotAddParens()
    {
        var (sql, _) = DapperHelper.ToSql<User>(u => u.Name == "Pedro" || (u.RoleId == 2 && u.GymId == 1));

        Assert.Equal("Name = @param0 OR RoleId = @param1 AND GymId = @param2", sql);
    }

    [Fact]
    public void ToSql_WhenUsingStringContains_ShouldTranslateToLike()
    {
        var (sql, parameters) = DapperHelper.ToSql<User>(u => u.Name!.Contains("Pedro"));

        Assert.Equal("Name LIKE @param0 ESCAPE '!'", sql);
        Assert.Equal("%Pedro%", parameters.Get<string>("@param0"));
    }

    [Fact]
    public void ToSql_WhenContainsValueHasWildcards_ShouldEscapeThem()
    {
        var (sql, parameters) = DapperHelper.ToSql<User>(u => u.Name!.Contains("100%_!"));

        Assert.Equal("Name LIKE @param0 ESCAPE '!'", sql);
        Assert.Equal("%100!%!_!!%", parameters.Get<string>("@param0"));
    }

    [Fact]
    public void ToSql_WhenRightOperandIsAMethodCall_ShouldEvaluateIt()
    {
        var (sql, parameters) = DapperHelper.ToSql<User>(u => u.Name == string.Concat("Pe", "dro"));

        Assert.Equal("Name = @param0", sql);
        Assert.Equal("Pedro", parameters.Get<string>("@param0"));
    }

    [Fact]
    public void ToSql_WhenRightOperandIsArithmetic_ShouldEvaluateIt()
    {
        var roleId = 2;

        var (sql, parameters) = DapperHelper.ToSql<User>(u => u.RoleId == roleId + 1);

        Assert.Equal("RoleId = @param0", sql);
        Assert.Equal(3, parameters.Get<int>("@param0"));
    }

    [Fact]
    public void ToSql_WhenMethodCallIsNotContains_ShouldThrowNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => DapperHelper.ToSql<User>(u => u.Name!.StartsWith("Pedro")));
    }

    [Fact]
    public void ToSql_WhenComparingToNull_ShouldUseIsNull()
    {
        var (sql, parameters) = DapperHelper.ToSql<User>(u => u.Name == null);

        Assert.Equal("Name IS NULL", sql);
        Assert.Empty(parameters.ParameterNames);
    }

    [Fact]
    public void ToSql_WhenComparingToNullWithNotEqual_ShouldUseIsNotNull()
    {
        var (sql, _) = DapperHelper.ToSql<User>(u => u.Name != null);

        Assert.Equal("Name IS NOT NULL", sql);
    }

    [Fact]
    public void ToSql_WhenComparingNullableToNull_ShouldUseIsNull()
    {
        var (sql, _) = DapperHelper.ToSql<Role>(r => r.Code == null);

        Assert.Equal("Code IS NULL", sql);
    }

    [Fact]
    public void ToSql_WhenNegatingAComparison_ShouldEmitNot()
    {
        var (sql, parameters) = DapperHelper.ToSql<User>(u => !(u.RoleId == 2));

        Assert.Equal("NOT (RoleId = @param0)", sql);
        Assert.Equal(2, parameters.Get<int>("@param0"));
    }

    [Fact]
    public void ToSql_WhenNegatingALogicalGroup_ShouldWrapIt()
    {
        var (sql, _) = DapperHelper.ToSql<User>(u => !(u.RoleId == 2 || u.RoleId == 3));

        Assert.Equal("NOT (RoleId = @param0 OR RoleId = @param1)", sql);
    }

    [Fact]
    public void ToSql_WhenNegatingABooleanColumn_ShouldCompareIt()
    {
        var (sql, parameters) = DapperHelper.ToSql<Role>(r => !r.IsSystemRole);

        Assert.Equal("NOT (IsSystemRole = @param0)", sql);
        Assert.True(parameters.Get<bool>("@param0"));
    }

    [Fact]
    public void ToSql_WhenPredicateIsABareBooleanColumn_ShouldCompareIt()
    {
        var (sql, parameters) = DapperHelper.ToSql<Role>(r => r.IsSystemRole);

        Assert.Equal("IsSystemRole = @param0", sql);
        Assert.True(parameters.Get<bool>("@param0"));
    }

    [Fact]
    public void ToSql_WhenPredicateIsABooleanConstant_ShouldEmitOneEqualsOne()
    {
        var (sql, _) = DapperHelper.ToSql<User>(_ => true);

        Assert.Equal("1 = 1", sql);
    }

    [Fact]
    public void ToSql_WhenPredicateIsACapturedBoolean_ShouldEvaluateIt()
    {
        var onlyActive = true;

        var (sql, _) = DapperHelper.ToSql<User>(_ => onlyActive);

        Assert.Equal("1 = 1", sql);
    }

    [Fact]
    public void ToSql_WhenLeftOperandIsNotAColumn_ShouldThrowNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => DapperHelper.ToSql<User>(u => "Pedro" == u.Name));
    }

    [Fact]
    public void ToSql_WhenComparingColumnToColumn_ShouldThrowNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => DapperHelper.ToSql<Role>(r => r.Name == r.Key));
    }

    [Fact]
    public void ToSql_WhenMemberPathIsNested_ShouldThrowNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => DapperHelper.ToSql<User>(u => u.Role!.Name == "Pedro"));
    }

    [Fact]
    public void ToSql_WhenMemberIsNotAColumn_ShouldThrowNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => DapperHelper.ToSql<User>(u => u.Name!.Length > 3));
    }

    [Fact]
    public void ToSql_WhenContainsReceiverIsNotAColumn_ShouldThrowNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => DapperHelper.ToSql<User>(_ => "abc".Contains("a")));
    }

    [Fact]
    public void ToSql_WhenPredicateIsNull_ShouldThrowArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => DapperHelper.ToSql<User>(null!));
    }
}
