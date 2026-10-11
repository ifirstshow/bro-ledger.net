using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BroLedger.Core.Tests.Helpers;

// Minimal HTTP stub for RoleShareServiceTests — the .NET stand-in for Java's
// com.sun.net.httpserver.HttpServer (tests are serial, so a single-threaded
// accept loop is enough). Parses only the request line plus X-Owner-Token and
// Content-Length headers; requests hitting no registered route get a 500.
public sealed class HttpTestServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Thread _acceptThread;
    private readonly object _gate = new();
    private readonly Dictionary<(string Method, string Path),
        Func<(string Method, string Path, string OwnerToken), (int Status, string Body)>> _routes = [];
    private volatile bool _disposed;

    /// <summary>Base URL of the stub, e.g. http://127.0.0.1:54321.</summary>
    public string Url { get; }

    public HttpTestServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true };
        _acceptThread.Start();
    }

    /// <summary>Registers a handler for one (method, path) — later registrations win.</summary>
    public void Route(string method, string path,
        Func<(string Method, string Path, string OwnerToken), (int Status, string Body)> handler)
    {
        lock (_gate) _routes[(method, path)] = handler;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _listener.Stop();
    }

    private void AcceptLoop()
    {
        while (!_disposed)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (Exception) when (_disposed)
            {
                return; // listener stopped — normal shutdown
            }
            using (client) HandleClient(client);
        }
    }

    private void HandleClient(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            var (method, path, ownerToken, contentLength) = ParseRequest(stream);
            DrainBody(stream, contentLength);

            Func<(string Method, string Path, string OwnerToken), (int Status, string Body)>? handler;
            lock (_gate) _routes.TryGetValue((method, path), out handler);
            var (status, body) = handler != null
                ? handler((method, path, ownerToken))
                : (500, "");
            WriteResponse(stream, status, body);
        }
        catch
        {
            // client vanished mid-request — nothing sensible to answer
        }
    }

    private static (string Method, string Path, string OwnerToken, int ContentLength) ParseRequest(NetworkStream stream)
    {
        string method = "", path = "", ownerToken = "";
        var contentLength = 0;
        var requestLine = true;
        string? line;
        while (!string.IsNullOrEmpty(line = ReadLine(stream)))
        {
            if (requestLine)
            {
                requestLine = false;
                var parts = line.Split(' ');
                if (parts.Length >= 2)
                {
                    method = parts[0];
                    path = parts[1];
                }
            }
            else if (line.StartsWith("X-Owner-Token:", StringComparison.OrdinalIgnoreCase))
            {
                ownerToken = line["X-Owner-Token:".Length..].Trim();
            }
            else if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                _ = int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
            }
        }
        return (method, path, ownerToken, contentLength);
    }

    private static string? ReadLine(NetworkStream stream)
    {
        var bytes = new List<byte>();
        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0) return bytes.Count == 0 ? null : Encoding.ASCII.GetString([.. bytes]);
            if (b == '\n') return Encoding.ASCII.GetString([.. bytes]).TrimEnd('\r');
            bytes.Add((byte)b);
        }
    }

    // Drain the request body before responding so the client's send always completes
    // (closing with unread bytes in flight can reset the connection mid-response).
    private static void DrainBody(NetworkStream stream, int length)
    {
        for (var remaining = length; remaining > 0; )
        {
            var buffer = new byte[Math.Min(remaining, 8192)];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0) return;
            remaining -= read;
        }
    }

    private static void WriteResponse(NetworkStream stream, int status, string body)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} \r\n" +
            "Content-Type: application/json\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n");
        stream.Write(head, 0, head.Length);
        if (payload.Length > 0) stream.Write(payload, 0, payload.Length);
    }
}
