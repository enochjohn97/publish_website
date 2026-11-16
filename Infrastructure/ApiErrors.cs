using System.Text.Json;
using System.Text.Json.Serialization;

namespace Folio.Web.Infrastructure;

/// <summary>Error carrying a safe user-facing message; internals stay in the server log.</summary>
public sealed class ApiException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public Dictionary<string, string>? Fields { get; }

    public ApiException(int status, string code, string message, Dictionary<string, string>? fields = null, Exception? inner = null)
        : base(message, inner)
    {
        Status = status;
        Code = code;
        Fields = fields;
    }

    public static ApiException Validation(string message, Dictionary<string, string>? fields = null) =>
        new(400, "VALIDATION_ERROR", message, fields);

    public static ApiException Unauthorized(string message = "Please sign in to continue.") =>
        new(401, "UNAUTHORIZED", message);

    public static ApiException Forbidden(string message = "You don't have access to that.") =>
        new(403, "FORBIDDEN", message);

    public static ApiException NotFound(string message = "We couldn't find that. It may have moved or been removed.") =>
        new(404, "NOT_FOUND", message);

    public static ApiException Conflict(string message) => new(409, "CONFLICT", message);

    public static ApiException RateLimited(string message) => new(429, "RATE_LIMITED", message);
}

public sealed record ApiError(string Code, string Message, Dictionary<string, string>? Fields);

public sealed record ApiResponse(bool Ok, object? Data, ApiError? Error)
{
    public static ApiResponse Success(object? data) => new(true, data, null);

    public static ApiResponse Fail(ApiException exception) =>
        new(false, null, new ApiError(exception.Code, exception.Message, exception.Fields));

    public static ApiResponse Fail(string code, string message, Dictionary<string, string>? fields = null) =>
        new(false, null, new ApiError(code, message, fields));

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonDefaults.Options);
    }
}

public sealed class PageResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Total { get; init; }
    public string? Notice { get; init; }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

/// <summary>Small in-process sliding-window limiter for credential and comment endpoints.</summary>
public sealed class RateLimiter
{
    private readonly Dictionary<string, (int Count, DateTime ResetAt)> _buckets = new();
    private readonly object _gate = new();

    public void Check(string key, int limit, TimeSpan window)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (!_buckets.TryGetValue(key, out var bucket) || bucket.ResetAt <= now)
            {
                _buckets[key] = (1, now.Add(window));
                return;
            }
            if (bucket.Count >= limit)
            {
                var seconds = Math.Max(1, (int)(bucket.ResetAt - now).TotalSeconds);
                throw ApiException.RateLimited($"Too many attempts. Please wait {seconds}s and try again.");
            }
            _buckets[key] = (bucket.Count + 1, bucket.ResetAt);
        }
    }
}
