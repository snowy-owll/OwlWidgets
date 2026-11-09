using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using OwlWidgets.Contracts;

namespace OwlWidgets.Protocol;

public static class CommandFrame
{
    public const int MaxBodyBytes = 256 * 1024;
    public const int MaxTextBytes = 256;

    public static byte[] Encode(WidgetCommand command)
    {
        var code = Encoding.UTF8.GetBytes(command.Code);
        var target = Encoding.UTF8.GetBytes(command.Target);
        if (code.Length == 0 || code.Length > MaxTextBytes)
            throw new InvalidOperationException("Invalid command code length.");
        if (target.Length == 0 || target.Length > MaxTextBytes)
            throw new InvalidOperationException("Invalid command target length.");
        if (command.Body.Length > MaxBodyBytes)
            throw new InvalidOperationException("Command body exceeds 256 KB.");

        var body = command.Body;
        var buffer = new byte[12 + code.Length + target.Length + body.Length];
        var offset = 0;
        Write(buffer, ref offset, code);
        Write(buffer, ref offset, target);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), body.Length);
        offset += 4;
        body.Span.CopyTo(buffer.AsSpan(offset));
        return buffer;
    }

    public static async Task WriteAsync(PipeStream stream, WidgetCommand command, CancellationToken cancellationToken)
    {
        var bytes = Encode(command);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Null: the other side closed the pipe, the usual end of a channel, so it is not an exception.</summary>
    public static async Task<WidgetCommand?> ReadAsync(PipeStream stream, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[8192];
        do
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return null;
            message.Write(buffer, 0, read);
        }
        while (!stream.IsMessageComplete);

        return Decode(message.GetBuffer().AsSpan(0, (int)message.Length));
    }

    public static WidgetCommand Decode(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        var code = ReadText(data, ref offset);
        var target = ReadText(data, ref offset);
        if (offset + 4 > data.Length)
            throw new InvalidOperationException("The command frame is truncated.");
        var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset));
        offset += 4;
        if (bodyLength < 0 || bodyLength > MaxBodyBytes || offset + bodyLength != data.Length)
            throw new InvalidOperationException("Invalid command body length.");
        var body = bodyLength == 0 ? ReadOnlyMemory<byte>.Empty : data.Slice(offset, bodyLength).ToArray();
        return new WidgetCommand(code, target, body);
    }

    private static void Write(byte[] buffer, ref int offset, byte[] text)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), text.Length);
        offset += 4;
        text.CopyTo(buffer.AsSpan(offset));
        offset += text.Length;
    }

    private static string ReadText(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset + 4 > data.Length)
            throw new InvalidOperationException("The command frame is truncated.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset));
        offset += 4;
        if (length <= 0 || length > MaxTextBytes || offset + length > data.Length)
            throw new InvalidOperationException("Invalid text length in the command frame.");
        var text = Encoding.UTF8.GetString(data.Slice(offset, length));
        offset += length;
        return text;
    }
}
