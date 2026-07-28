using System.Collections.ObjectModel;
using System.Text.Json;

namespace LibraTray.Core.Protocol;

public abstract class YeelightMessage
{
    private protected YeelightMessage()
    {
    }
}

public abstract class YeelightResponse : YeelightMessage
{
    private protected YeelightResponse(long id)
    {
        Id = id;
    }

    public long Id { get; }
}

public sealed class YeelightSuccessResponse : YeelightResponse
{
    internal YeelightSuccessResponse(long id, IReadOnlyList<JsonElement> results)
        : base(id)
    {
        Results = results;
    }

    public IReadOnlyList<JsonElement> Results { get; }
}

public sealed class YeelightErrorResponse : YeelightResponse
{
    internal YeelightErrorResponse(
        long id,
        int code,
        string message,
        JsonElement? data)
        : base(id)
    {
        Code = code;
        Message = message;
        Data = data;
    }

    public int Code { get; }

    public string Message { get; }

    public JsonElement? Data { get; }
}

public abstract class YeelightNotification : YeelightMessage
{
    private protected YeelightNotification(string method)
    {
        Method = method;
    }

    public string Method { get; }
}

public sealed class YeelightPropsNotification : YeelightNotification
{
    internal YeelightPropsNotification(
        IReadOnlyDictionary<string, JsonElement> properties)
        : base("props")
    {
        Properties = properties;
    }

    public IReadOnlyDictionary<string, JsonElement> Properties { get; }
}

public sealed class YeelightUnknownNotification : YeelightNotification
{
    internal YeelightUnknownNotification(string method, JsonElement? parameters)
        : base(method)
    {
        Parameters = parameters;
    }

    public JsonElement? Parameters { get; }
}

public static class YeelightMessageParser
{
    public static YeelightMessage Parse(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new YeelightProtocolException("The protocol message is empty.");
        }

        if (payload.Length > CrlfMessageFramer.DefaultMaximumFrameBytes)
        {
            throw new YeelightFrameTooLargeException(
                CrlfMessageFramer.DefaultMaximumFrameBytes);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                payload,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });

            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new YeelightProtocolException(
                    "A protocol message must be a JSON object.");
            }

            if (root.TryGetProperty("id", out JsonElement idElement))
            {
                return ParseResponse(root, idElement);
            }

            if (root.TryGetProperty("method", out JsonElement methodElement))
            {
                return ParseNotification(root, methodElement);
            }

            throw new YeelightProtocolException(
                "A protocol message contains neither an ID nor a method.");
        }
        catch (JsonException exception)
        {
            throw new YeelightProtocolException(
                "The protocol message contains invalid JSON.",
                exception);
        }
    }

    private static YeelightResponse ParseResponse(
        JsonElement root,
        JsonElement idElement)
    {
        if (idElement.ValueKind != JsonValueKind.Number
            || !idElement.TryGetInt64(out long id)
            || id <= 0)
        {
            throw new YeelightProtocolException(
                "A response ID must be a positive integer.");
        }

        if (root.TryGetProperty("error", out JsonElement errorElement))
        {
            return ParseError(id, errorElement);
        }

        if (!root.TryGetProperty("result", out JsonElement resultElement)
            || resultElement.ValueKind != JsonValueKind.Array)
        {
            throw new YeelightProtocolException(
                "A successful response must contain a result array.");
        }

        var results = new List<JsonElement>();
        foreach (JsonElement result in resultElement.EnumerateArray())
        {
            results.Add(result.Clone());
        }

        return new YeelightSuccessResponse(id, results.AsReadOnly());
    }

    private static YeelightErrorResponse ParseError(long id, JsonElement errorElement)
    {
        if (errorElement.ValueKind != JsonValueKind.Object
            || !errorElement.TryGetProperty("code", out JsonElement codeElement)
            || codeElement.ValueKind != JsonValueKind.Number
            || !codeElement.TryGetInt32(out int code)
            || !errorElement.TryGetProperty("message", out JsonElement messageElement)
            || messageElement.ValueKind != JsonValueKind.String)
        {
            throw new YeelightProtocolException(
                "An error response must contain an integer code and a string message.");
        }

        JsonElement? data = null;
        if (errorElement.TryGetProperty("data", out JsonElement dataElement))
        {
            data = dataElement.Clone();
        }

        return new YeelightErrorResponse(
            id,
            code,
            messageElement.GetString() ?? string.Empty,
            data);
    }

    private static YeelightNotification ParseNotification(
        JsonElement root,
        JsonElement methodElement)
    {
        if (methodElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(methodElement.GetString()))
        {
            throw new YeelightProtocolException(
                "A notification method must be a non-empty string.");
        }

        string method = methodElement.GetString()!;
        bool hasParameters = root.TryGetProperty(
            "params",
            out JsonElement parametersElement);

        if (string.Equals(method, "props", StringComparison.Ordinal))
        {
            if (!hasParameters
                || parametersElement.ValueKind != JsonValueKind.Object)
            {
                throw new YeelightProtocolException(
                    "A props notification must contain a params object.");
            }

            var properties = new Dictionary<string, JsonElement>(
                StringComparer.Ordinal);

            foreach (JsonProperty property in parametersElement.EnumerateObject())
            {
                properties[property.Name] = property.Value.Clone();
            }

            return new YeelightPropsNotification(
                new ReadOnlyDictionary<string, JsonElement>(properties));
        }

        return new YeelightUnknownNotification(
            method,
            hasParameters ? parametersElement.Clone() : null);
    }
}
