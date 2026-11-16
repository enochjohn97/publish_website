using Dapper;
using MySqlConnector;

namespace Folio.Web.Data;

/// <summary>Thin ADO.NET/Dapper gateway. Every call is parameterized; no SQL is built from user text.</summary>
public sealed class Db
{
    private readonly string _connectionString;

    public Db(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
    }

    public async Task<MySqlConnection> OpenAsync()
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task<List<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = await OpenAsync();
        var rows = await connection.QueryAsync<T>(sql, parameters);
        return rows.AsList();
    }

    public async Task<T?> FirstAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = await OpenAsync();
        return await connection.QueryFirstOrDefaultAsync<T>(sql, parameters);
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = await OpenAsync();
        var rows = await connection.ExecuteAsync(sql, parameters);
        return rows;
    }

    public async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = await OpenAsync();
        var value = await connection.ExecuteScalarAsync<T>(sql, parameters);
        return value ?? throw new InvalidOperationException("Scalar query returned no value.");
    }

    public async Task<T> TransactionAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> work)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var result = await work(connection, transaction);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
