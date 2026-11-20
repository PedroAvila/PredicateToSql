using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace PAN.DapperLambdaToSql.Tests.Fakes;

/// <summary>
/// Se lanza en cuanto el comando va a ejecutarse, ya con el <c>CommandText</c> definitivo.
/// Evita tener que simular un <see cref="DbDataReader"/> completo solo para leer el SQL.
/// </summary>
public class SqlCapturedException(
    string sql,
    CancellationToken? cancellationToken = null,
    DbTransaction? transaction = null) : Exception("SQL capturado")
{
    public string Sql { get; } = sql;
    public CancellationToken? CancellationToken { get; } = cancellationToken;
    public DbTransaction? Transaction { get; } = transaction;
}

public abstract class CapturingConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;
    private string _connectionString = string.Empty;

    // La base usa [AllowNull]: el getter no devuelve null, pero el setter lo acepta.
    [AllowNull]
    public override string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = value ?? string.Empty;
    }
    public override string Database => "test";
    public override string DataSource => "test";
    public override string ServerVersion => "0.0";
    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => _state = ConnectionState.Closed;
    public override void Open() => _state = ConnectionState.Open;

    protected override DbTransaction BeginDbTransaction(IsolationLevel il) => throw new NotSupportedException();

    protected override DbCommand CreateDbCommand() => new CapturingCommand { Connection = this };
}

// El nombre del tipo es lo único que mira DapperHelper.GetDialect, así que estas clases
// reproducen fielmente la detección real sin depender de los paquetes de proveedor.
public sealed class SqlConnection : CapturingConnection;
public sealed class MySqlConnection : CapturingConnection;
public sealed class NpgsqlConnection : CapturingConnection;

/// <summary>Transacción mínima para verificar que Dapper recibe la que se le pasó.</summary>
public sealed class CapturingTransaction : DbTransaction
{
    public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;
    protected override DbConnection DbConnection => throw new NotSupportedException();

    public override void Commit() { }
    public override void Rollback() { }
}

public sealed class CapturingCommand : DbCommand
{
    private string _commandText = string.Empty;

    [AllowNull]
    public override string CommandText
    {
        get => _commandText;
        set => _commandText = value ?? string.Empty;
    }
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    public override bool DesignTimeVisible { get; set; }

    protected override DbConnection? DbConnection { get; set; }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new CapturingParameterCollection();

    public override void Cancel() { }
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new CapturingParameter();

    private SqlCapturedException Captured() => new(CommandText, null, DbTransaction);

    private SqlCapturedException Captured(CancellationToken token) => new(CommandText, token, DbTransaction);

    public override int ExecuteNonQuery() => throw Captured();
    public override object? ExecuteScalar() => throw Captured();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw Captured();

    // Los overloads async conservan el token y la transacción para que los tests
    // verifiquen que la librería los propaga hasta Dapper.
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        => throw Captured(cancellationToken);

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        => throw Captured(cancellationToken);

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior, CancellationToken cancellationToken)
        => throw Captured(cancellationToken);
}

public sealed class CapturingParameter : DbParameter
{
    private string _parameterName = string.Empty;
    private string _sourceColumn = string.Empty;

    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    [AllowNull]
    public override string ParameterName
    {
        get => _parameterName;
        set => _parameterName = value ?? string.Empty;
    }
    public override int Size { get; set; }
    [AllowNull]
    public override string SourceColumn
    {
        get => _sourceColumn;
        set => _sourceColumn = value ?? string.Empty;
    }
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }
    public override void ResetDbType() { }
}

public sealed class CapturingParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _items = [];

    public override int Count => _items.Count;
    public override object SyncRoot => _items;

    public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
    public override void AddRange(Array values) { foreach (var v in values) Add(v!); }
    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => _items.Contains((DbParameter)value);
    public override bool Contains(string value) => IndexOf(value) >= 0;
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();
    public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
    public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
    public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _items.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));

    protected override DbParameter GetParameter(int index) => _items[index];
    protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
    protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
}
