using System.Buffers;
using System.Text.Json;

namespace LibraTray.Core.Protocol;

public static class YeelightRequestSerializer
{
    public const int MaximumMethodLength = 128;

    public static byte[] Serialize(YeelightRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var output = new ArrayBufferWriter<byte>();

        try
        {
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", request.Id);
                writer.WriteString("method", request.Method);
                writer.WritePropertyName("params");
                writer.WriteStartArray();

                foreach (object? parameter in request.Parameters)
                {
                    if (parameter is null)
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        JsonSerializer.Serialize(
                            writer,
                            parameter,
                            parameter.GetType());
                    }
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "A request parameter could not be serialized as JSON.",
                nameof(request),
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new ArgumentException(
                "A request parameter uses an unsupported JSON type.",
                nameof(request),
                exception);
        }

        if (output.WrittenCount > CrlfMessageFramer.DefaultMaximumFrameBytes)
        {
            throw new YeelightFrameTooLargeException(
                CrlfMessageFramer.DefaultMaximumFrameBytes);
        }

        byte[] frame = new byte[output.WrittenCount + 2];
        output.WrittenSpan.CopyTo(frame);
        frame[^2] = (byte)'\r';
        frame[^1] = (byte)'\n';
        return frame;
    }

    public static void Validate(YeelightRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Id <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Id,
                "A request ID must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.Method)
            || request.Method.Length > MaximumMethodLength
            || !IsValidMethod(request.Method))
        {
            throw new ArgumentException(
                "A method must start with an ASCII letter and contain only ASCII letters, digits, or underscores.",
                nameof(request));
        }
    }

    private static bool IsValidMethod(string method)
    {
        if (!IsAsciiLetter(method[0]))
        {
            return false;
        }

        foreach (char character in method)
        {
            if (!IsAsciiLetter(character)
                && !char.IsAsciiDigit(character)
                && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char character) =>
        character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
}
