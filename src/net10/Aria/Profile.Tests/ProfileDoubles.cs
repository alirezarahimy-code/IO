using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

// Aria-only in-memory ADO.NET fakes. Commands return fixed DataTable rows:
// they do NOT execute SQL or authenticate against a real SQL Server.
internal sealed class TableConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;
    private readonly DataTable _data;
    public List<TableCommand> Commands { get; } = new();
    public Exception? OpenFailure { get; set; }
    public Exception? ReadFailure { get; set; }
    public Action? BeforeOpen { get; set; }
    public bool WasDisposed { get; private set; }
    public override string Database { get; }
    [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
    public override string DataSource => "in-memory";
    public override string ServerVersion => "in-memory";
    public override ConnectionState State => _state;

    public TableConnection(string database, DataTable data)
    {
        Database = database;
        _data = data;
    }

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        BeforeOpen?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (OpenFailure != null) throw OpenFailure;
        _state = ConnectionState.Open;
        return Task.CompletedTask;
    }
    public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
    public override void Close() => _state = ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    protected override DbCommand CreateDbCommand()
    {
        var command = new TableCommand(this, _data);
        Commands.Add(command);
        return command;
    }
    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        Close();
        base.Dispose(disposing);
    }
}

internal sealed class TableCommand : DbCommand
{
    private readonly TableConnection _connection;
    private readonly DataTable _data;
    private readonly TableParameters _parameters = new();
    public bool WasDisposed { get; private set; }
    [AllowNull] public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    [AllowNull] protected override DbConnection DbConnection
    {
        get => _connection;
        set { if (!ReferenceEquals(value, _connection)) throw new InvalidOperationException("Other connection"); }
    }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection => _parameters;

    public TableCommand(TableConnection connection, DataTable data)
    {
        _connection = connection;
        _data = data;
    }
    public override void Cancel() => throw new NotSupportedException();
    public override int ExecuteNonQuery() => throw new InvalidOperationException("Writes are forbidden in this fixture");
    public override object ExecuteScalar() => throw new InvalidOperationException("Unexpected scalar query");
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new TableParameter();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        if (_connection.ReadFailure != null) throw _connection.ReadFailure;
        return _data.CreateDataReader();
    }
    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        base.Dispose(disposing);
    }
}

internal sealed class TableParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public override bool IsNullable { get; set; }
    [AllowNull] public override string ParameterName { get; set; } = string.Empty;
    [AllowNull] public override string SourceColumn { get; set; } = string.Empty;
    [AllowNull] public override object Value { get; set; } = DBNull.Value;
    public override bool SourceColumnNullMapping { get; set; }
    public override int Size { get; set; }
    public override void ResetDbType() => DbType = DbType.Object;
}

internal sealed class TableParameters : DbParameterCollection
{
    private readonly List<DbParameter> _items = new();
    public override int Count => _items.Count;
    public override object SyncRoot => this;
    public override bool IsFixedSize => false;
    public override bool IsReadOnly => false;
    public override bool IsSynchronized => false;
    public override int Add(object value)
    {
        if (value is not DbParameter parameter) throw new ArgumentException("Only DbParameter accepted", nameof(value));
        _items.Add(parameter);
        return _items.Count - 1;
    }
    public override void AddRange(Array values)
    {
        foreach (object value in values) Add(value);
    }
    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => value is DbParameter parameter && _items.Contains(parameter);
    public override bool Contains(string value) => IndexOf(value) >= 0;
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();
    public override int IndexOf(object value) => value is DbParameter parameter ? _items.IndexOf(parameter) : -1;
    public override int IndexOf(string parameterName) => _items.FindIndex(item => item.ParameterName == parameterName);
    public override void Insert(int index, object value)
    {
        if (value is not DbParameter parameter) throw new ArgumentException("Only DbParameter accepted", nameof(value));
        _items.Insert(index, parameter);
    }
    public override void Remove(object value)
    {
        if (value is DbParameter parameter) _items.Remove(parameter);
    }
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));
    protected override DbParameter GetParameter(int index) => _items[index];
    protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
    protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) => SetParameter(IndexOf(parameterName), value);
}
