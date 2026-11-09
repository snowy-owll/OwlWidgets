using System.Text.Json;

namespace OwlWidgets.Contracts;

public static class WidgetCommandJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ReadOnlyMemory<byte> Serialize<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T? Deserialize<T>(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
            return default;
        return JsonSerializer.Deserialize<T>(body.Span, Options);
    }

    public static string? ReadUtf8(ReadOnlyMemory<byte> body) =>
        body.IsEmpty ? null : System.Text.Encoding.UTF8.GetString(body.Span);
}
