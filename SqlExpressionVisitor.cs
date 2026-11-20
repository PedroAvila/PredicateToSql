using Dapper;
using System.Linq.Expressions;
using System.Text;

namespace PAN.DapperLambdaToSql;

/// <summary>
/// Traduce un predicado booleano a un fragmento WHERE y sus parámetros. El lado
/// izquierdo de cada comparación debe ser una propiedad simple de la entidad (una
/// columna); el derecho se evalúa como valor. Ver docs/adr/0005.
/// </summary>
internal sealed class SqlExpressionVisitor
{
    private readonly ISqlDialect _dialect;
    private readonly ParameterExpression _parameter;
    private readonly StringBuilder _sql = new();
    private int _nextParameter;

    public string Sql => _sql.ToString();
    public DynamicParameters Parameters { get; } = new();

    public SqlExpressionVisitor(ISqlDialect dialect, ParameterExpression parameter)
    {
        _dialect = dialect ?? SqlDialects.None;
        _parameter = parameter ?? throw new ArgumentNullException(nameof(parameter));
    }

    public void VisitPredicate(Expression expression)
    {
        switch (expression)
        {
            case LambdaExpression lambda:
                VisitPredicate(lambda.Body);
                return;

            case UnaryExpression { NodeType: ExpressionType.Not } not:
                _sql.Append("NOT (");
                VisitPredicate(not.Operand);
                _sql.Append(')');
                return;

            case UnaryExpression { NodeType: ExpressionType.Convert } convert:
                VisitPredicate(convert.Operand);
                return;

            case UnaryExpression { NodeType: ExpressionType.ConvertChecked } convertChecked:
                VisitPredicate(convertChecked.Operand);
                return;

            case BinaryExpression { NodeType: ExpressionType.AndAlso } andAlso:
                VisitLogicalBinary(andAlso, " AND ");
                return;

            case BinaryExpression { NodeType: ExpressionType.OrElse } orElse:
                VisitLogicalBinary(orElse, " OR ");
                return;

            case BinaryExpression comparison when IsComparison(comparison.NodeType):
                VisitComparisonBinary(comparison, ComparisonOperatorFor(comparison.NodeType));
                return;

            case MethodCallExpression call:
                VisitContains(call);
                return;

            case MemberExpression member when member.Type == typeof(bool):
                VisitBooleanMember(member);
                return;

            // u => true / u => false: un predicado constante no tiene columna, pero
            // tampoco puede quedar como WHERE @param -SQL Server lo rechaza-.
            case ConstantExpression { Value: bool value }:
                _sql.Append(value ? "1 = 1" : "1 = 0");
                return;

            default:
                throw new NotSupportedException(
                    $"El predicado contiene una expresión no soportada ('{expression.NodeType}'). " +
                    "Solo se admiten comparaciones, &&, ||, !, bool sueltos y string.Contains(...).");
        }
    }

    private static bool IsComparison(ExpressionType nodeType) => nodeType is
        ExpressionType.Equal or
        ExpressionType.NotEqual or
        ExpressionType.LessThan or
        ExpressionType.LessThanOrEqual or
        ExpressionType.GreaterThan or
        ExpressionType.GreaterThanOrEqual;

    private static string ComparisonOperatorFor(ExpressionType nodeType) => nodeType switch
    {
        ExpressionType.Equal => " = ",
        ExpressionType.NotEqual => " <> ",
        ExpressionType.LessThan => " < ",
        ExpressionType.LessThanOrEqual => " <= ",
        ExpressionType.GreaterThan => " > ",
        ExpressionType.GreaterThanOrEqual => " >= ",
        _ => throw new NotSupportedException($"Operador de comparación no soportado: '{nodeType}'."),
    };

    private void VisitComparisonBinary(BinaryExpression node, string sqlOperator)
    {
        VisitColumn(node.Left);

        var value = ResolveValue(node.Right);

        // En SQL, "= NULL" nunca es verdadero: la comparación con null necesita
        // IS NULL / IS NOT NULL para no devolver cero filas en silencio.
        if (value is null)
        {
            if (node.NodeType == ExpressionType.Equal)
            {
                _sql.Append(" IS NULL");
                return;
            }

            if (node.NodeType == ExpressionType.NotEqual)
            {
                _sql.Append(" IS NOT NULL");
                return;
            }

            throw new NotSupportedException(
                $"No se puede comparar con null usando '{sqlOperator.Trim()}'. Solo == y != admiten null.");
        }

        _sql.Append(sqlOperator);
        AddParameter(value);
    }

    private void VisitLogicalBinary(BinaryExpression node, string sqlOperator)
    {
        VisitLogicalOperand(node.Left, node.NodeType);
        _sql.Append(sqlOperator);
        VisitLogicalOperand(node.Right, node.NodeType);
    }

    private void VisitLogicalOperand(Expression operand, ExpressionType parentNodeType)
    {
        // Paréntesis mínimos: solo hace falta envolver un OrElse cuando cuelga
        // de un AndAlso, porque AND liga más fuerte que OR tanto en C# como en SQL.
        var needsParens = parentNodeType == ExpressionType.AndAlso
            && operand is BinaryExpression { NodeType: ExpressionType.OrElse };

        if (needsParens)
        {
            _sql.Append('(');
            VisitPredicate(operand);
            _sql.Append(')');
        }
        else
        {
            VisitPredicate(operand);
        }
    }

    private void VisitBooleanMember(MemberExpression member)
    {
        // Un bool suelto -u => u.IsActive- no es SQL válido en SQL Server: se
        // traduce a "columna = @param" con true, igual que haría EF.
        if (IsColumn(member))
        {
            VisitColumn(member);
            _sql.Append(" = ");
            AddParameter(true);
            return;
        }

        // Un bool capturado por el closure no depende de la entidad: se evalúa.
        if (!ReferencesParameter(member))
        {
            var value = (bool)ResolveValue(member)!;
            _sql.Append(value ? "1 = 1" : "1 = 0");
            return;
        }

        throw new NotSupportedException(
            "Solo se pueden usar propiedades simples de la entidad como predicado booleano. " +
            "No están soportadas las rutas anidadas.");
    }

    private void VisitContains(MethodCallExpression node)
    {
        if (!IsStringContains(node))
        {
            throw new NotSupportedException(
                $"El método '{node.Method.DeclaringType?.Name}.{node.Method.Name}' no está soportado. " +
                "Solo string.Contains(...) se traduce a LIKE.");
        }

        // El receptor debe ser una columna: "abc".Contains(...) no es SQL válido.
        VisitColumn(node.Object!);

        _sql.Append(" LIKE ");

        var rawValue = ResolveValue(node.Arguments[0]);
        AddParameter(rawValue is null ? null : ToLikePattern(rawValue));

        // '!' como carácter de escape es válido tal cual en SQL Server y MySQL, y
        // evita que %/_ del valor se interpreten como comodines.
        _sql.Append(" ESCAPE '!'");
    }

    /// <summary>
    /// Escapa los comodines de LIKE y envuelve el valor en <c>%...%</c>. El carácter
    /// de escape es <c>!</c> para no depender del backslash -que MySQL trata distinto
    /// según el modo-.
    /// </summary>
    private static string ToLikePattern(object value)
    {
        var text = value.ToString() ?? string.Empty;

        var escaped = text
            .Replace("!", "!!")
            .Replace("%", "!%")
            .Replace("_", "!_");

        return $"%{escaped}%";
    }

    private void VisitColumn(Expression expression)
    {
        var stripped = StripConvert(expression);

        if (stripped is not MemberExpression { Expression: ParameterExpression parameter } member
            || parameter != _parameter)
        {
            throw new NotSupportedException(
                "Solo se pueden usar propiedades simples de la entidad como columna. " +
                "No están soportados los valores constantes invertidos, las rutas anidadas " +
                "ni las propiedades calculadas en el cliente.");
        }

        _sql.Append(_dialect.Delimit(member.Member.Name));
    }

    private bool IsColumn(Expression expression)
        => StripConvert(expression) is MemberExpression { Expression: ParameterExpression parameter }
            && parameter == _parameter;

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            expression = unary.Operand;

        return expression;
    }

    private object? ResolveValue(Expression expression)
    {
        if (ReferencesParameter(expression))
        {
            throw new NotSupportedException(
                "No se soportan comparaciones entre dos columnas: el lado derecho debe ser un valor " +
                "(constante, variable capturada o una expresión que no dependa de la entidad).");
        }

        // Compilación tipada (evita DynamicInvoke). El árbol puede traer métodos,
        // aritmética y propiedades: se evalúa entero como cualquier expresión C#.
        var value = Expression.Lambda<Func<object?>>(
            Expression.Convert(expression, typeof(object))).Compile();

        return value();
    }

    private bool ReferencesParameter(Expression expression)
    {
        var finder = new ParameterFinder(_parameter);
        finder.Visit(expression);
        return finder.Found;
    }

    private void AddParameter(object? value)
    {
        var parameterName = $"@param{_nextParameter++}";
        Parameters.Add(parameterName, value);
        _sql.Append(parameterName);
    }

    private static bool IsStringContains(MethodCallExpression node)
        => node.Method.DeclaringType == typeof(string)
           && node.Method.Name == nameof(string.Contains)
           && node.Object != null
           && node.Arguments.Count == 1;

    private sealed class ParameterFinder(ParameterExpression parameter) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == parameter) Found = true;
            return node;
        }
    }
}
