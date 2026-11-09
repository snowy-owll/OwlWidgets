using System.Buffers.Binary;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;

namespace OwlWidgets.Widgets.InternetSpeed.Measurement;

/// <summary>
/// The connections of one test, for <see cref="SocketsHttpHandler.ConnectCallback"/> of any provider. Knows its sockets,
/// so an upload is counted by what the servers acknowledged, not by what was written into the send buffers.
/// </summary>
internal sealed class TestSockets
{
    /// <summary>
    /// With the default 64 KB buffer one connection to a server 140 ms away sends no more than 64 KB
    /// per round trip, under 4 Mbps, whatever the line: an upload over one or a few connections stays far below it.
    /// </summary>
    private const int Buffer = 8 << 20;

    /// <summary><c>SIO_TCP_INFO</c>, Windows 10 1703 and later: <c>TCP_INFO_v0</c> of a connected socket.</summary>
    private const int TcpInfoControl = unchecked((int)0xD8000027);

    private const int TcpInfoSize = 88;
    private const int BytesInFlightOffset = 28;
    private const int BytesOutOffset = 48;
    private const int BytesRetransOffset = 68;

    private readonly object _lock = new();
    private readonly List<Connection> _connections = [];
    private bool _unsupported;

    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
            SendBufferSize = Buffer,
            ReceiveBufferSize = Buffer
        };
        try
        {
            await socket.ConnectAsync(context.DnsEndPoint, token);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        lock (_lock)
            _connections.Add(new Connection(socket));
        return new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>
    /// Bytes the servers have acknowledged over all connections so far: sent, less those still in flight and
    /// the retransmitted ones. A closed connection keeps its last reading, so the total only grows.
    /// null when Windows does not report the TCP state; the caller then counts what it writes.
    /// </summary>
    public long? Acknowledged()
    {
        lock (_lock)
        {
            if (_unsupported)
                return null;
            var info = new byte[TcpInfoSize];
            long total = 0;
            foreach (var connection in _connections)
            {
                if (!connection.Closed)
                {
                    try
                    {
                        connection.Socket.IOControl(TcpInfoControl, BitConverter.GetBytes(0), info);
                        var sent = BinaryPrimitives.ReadInt64LittleEndian(info.AsSpan(BytesOutOffset));
                        var inFlight = BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(BytesInFlightOffset));
                        var resent = BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(BytesRetransOffset));
                        connection.Last = Math.Max(connection.Last, sent - inFlight - resent);
                    }
                    catch (ObjectDisposedException)
                    {
                        connection.Closed = true;
                    }
                    catch (SocketException exception) when (exception.SocketErrorCode
                        is SocketError.OperationNotSupported or SocketError.InvalidArgument or SocketError.ProtocolOption)
                    {
                        _unsupported = true;
                        return null;
                    }
                    catch (SocketException)
                    {
                        connection.Closed = true;
                    }
                }

                total += connection.Last;
            }

            return total;
        }
    }

    private sealed class Connection(Socket socket)
    {
        public Socket Socket { get; } = socket;

        public long Last { get; set; }

        public bool Closed { get; set; }
    }
}
