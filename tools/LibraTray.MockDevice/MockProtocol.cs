using System.Buffers;
using System.Text.Json;

namespace LibraTray.MockDevice;

internal sealed record MockRequest(
    int Id,
    string Method,
    IReadOnlyList<JsonElement> Parameters)
{
    public static bool TryParse(
        ReadOnlyMemory<byte> payload,
        out MockRequest? request,
        out string error)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out JsonElement idElement)
                || !idElement.TryGetInt32(out int id)
                || id <= 0
                || !root.TryGetProperty("method", out JsonElement methodElement)
                || methodElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(methodElement.GetString())
                || !root.TryGetProperty("params", out JsonElement parametersElement)
                || parametersElement.ValueKind != JsonValueKind.Array)
            {
                request = null;
                error = "请求必须包含正整数 id、字符串 method 和数组 params。";
                return false;
            }

            var parameters = new List<JsonElement>();
            foreach (JsonElement parameter in parametersElement.EnumerateArray())
            {
                parameters.Add(parameter.Clone());
            }

            request = new MockRequest(
                id,
                methodElement.GetString()!,
                parameters.AsReadOnly());
            error = string.Empty;
            return true;
        }
        catch (JsonException exception)
        {
            request = null;
            error = $"JSON 无效：{exception.Message}";
            return false;
        }
    }
}

internal static class MockProtocol
{
    public static byte[] CreateSuccess(int id, IReadOnlyList<string> results) =>
        CreateFrame(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WritePropertyName("result");
            writer.WriteStartArray();
            foreach (string result in results)
            {
                writer.WriteStringValue(result);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    public static byte[] CreateError(int id, int code, string message) =>
        CreateFrame(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        });

    public static byte[] CreateProps(IReadOnlyDictionary<string, string> properties) =>
        CreateFrame(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("method", "props");
            writer.WritePropertyName("params");
            writer.WriteStartObject();
            foreach ((string name, string value) in properties)
            {
                writer.WriteString(name, value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        });

    private static byte[] CreateFrame(Action<Utf8JsonWriter> writeJson)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writeJson(writer);
        }

        byte[] frame = new byte[buffer.WrittenCount + 2];
        buffer.WrittenSpan.CopyTo(frame);
        frame[^2] = (byte)'\r';
        frame[^1] = (byte)'\n';
        return frame;
    }
}
