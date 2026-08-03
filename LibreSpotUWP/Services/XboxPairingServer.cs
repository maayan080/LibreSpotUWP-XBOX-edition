using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace LibreSpotUWP.Services
{
    public sealed class XboxPairingServer : IDisposable
    {
        private const int FirstPort = 8080;
        private const int PortAttempts = 12;

        private StreamSocketListener _listener;
        private bool _disposed;

        public event EventHandler<string> SessionReceived;

        public string Url { get; private set; }

        public bool IsRunning => _listener != null;

        public async Task<bool> StartAsync()
        {
            if (_listener != null) { return true; }

            string host = GetLocalAddress();
            if (host == null)
            {
                LogService.Warn("XboxPairingServer: no local IPv4 address found.");
                return false;
            }

            for (int i = 0; i < PortAttempts; i++)
            {
                int port = FirstPort + i;
                var listener = new StreamSocketListener();
                listener.ConnectionReceived += OnConnectionReceived;

                try
                {
                    await listener.BindServiceNameAsync(port.ToString());
                    _listener = listener;
                    Url = "http://" + host + ":" + port + "/";
                    LogService.Info("XboxPairingServer listening on " + Url);
                    return true;
                }
                catch (Exception ex)
                {
                    listener.ConnectionReceived -= OnConnectionReceived;
                    listener.Dispose();

                    if (i == 0 && ex.HResult == unchecked((int)0x80070005))
                    {
                        LogService.Warn("XboxPairingServer: access denied - is privateNetworkClientServer declared?");
                        return false;
                    }
                }
            }

            LogService.Warn("XboxPairingServer: no free port in range.");
            return false;
        }

        public void Stop()
        {
            var listener = _listener;
            _listener = null;

            if (listener != null)
            {
                listener.ConnectionReceived -= OnConnectionReceived;
                listener.Dispose();
            }
            Url = null;
        }

        private static string GetLocalAddress()
        {
            try
            {
                var candidates = NetworkInformation.GetHostNames()
                    .Where(h => h.Type == HostNameType.Ipv4)
                    .Where(h => h.IPInformation != null)
                    .Select(h => h.CanonicalName)
                    .Where(n => !string.IsNullOrEmpty(n) && !n.StartsWith("127."))
                    .ToList();

                return candidates.FirstOrDefault(n =>
                           n.StartsWith("192.168.") || n.StartsWith("10.") || n.StartsWith("172."))
                       ?? candidates.FirstOrDefault();
            }
            catch (Exception ex)
            {
                LogService.Warn("XboxPairingServer: address lookup failed - " + ex.Message);
                return null;
            }
        }

        private async void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            try
            {
                string request = await ReadRequestAsync(args.Socket);
                if (string.IsNullOrEmpty(request)) { return; }

                bool isPost = request.StartsWith("POST", StringComparison.OrdinalIgnoreCase);

                if (!isPost)
                {
                    await RespondAsync(args.Socket, BuildFormPage());
                    return;
                }

                string body = ExtractBody(request);
                string session = ParseFormField(body, "session");

                if (string.IsNullOrWhiteSpace(session))
                {
                    await RespondAsync(args.Socket, BuildResultPage(false, "No sign-in details were submitted."));
                    return;
                }

                await RespondAsync(args.Socket, BuildResultPage(true, "Sent to your Xbox. You can close this page."));
                SessionReceived?.Invoke(this, session);
            }
            catch (Exception ex)
            {
                LogService.Warn("XboxPairingServer: request failed - " + ex.Message);
            }
            finally
            {
                try { args.Socket.Dispose(); } catch { }
            }
        }

        private static async Task<string> ReadRequestAsync(StreamSocket socket)
        {
            var reader = new DataReader(socket.InputStream)
            {
                InputStreamOptions = InputStreamOptions.Partial
            };

            var text = new StringBuilder();

            uint read = await reader.LoadAsync(8192);
            if (read == 0) { return null; }
            text.Append(reader.ReadString(read));

            int contentLength = GetContentLength(text.ToString());
            int headerEnd = text.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal);

            while (contentLength > 0 && headerEnd >= 0 &&
                   text.Length - (headerEnd + 4) < contentLength)
            {
                read = await reader.LoadAsync(8192);
                if (read == 0) { break; }
                text.Append(reader.ReadString(read));
            }

            reader.DetachStream();
            return text.ToString();
        }

        private static int GetContentLength(string request)
        {
            foreach (var line in request.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    int value;
                    if (int.TryParse(line.Substring(15).Trim(), out value)) { return value; }
                }
                if (line.Length == 0) { break; }
            }
            return 0;
        }

        private static string ExtractBody(string request)
        {
            int at = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            return at < 0 ? string.Empty : request.Substring(at + 4);
        }

        private static string ParseFormField(string body, string name)
        {
            if (string.IsNullOrEmpty(body)) { return null; }

            foreach (var pair in body.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) { continue; }

                if (string.Equals(pair.Substring(0, eq), name, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pair.Substring(eq + 1).Replace("+", " "));
                }
            }
            return null;
        }

        private static async Task RespondAsync(StreamSocket socket, string html)
        {
            byte[] payload = Encoding.UTF8.GetBytes(html);

            string headers =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                "Content-Length: " + payload.Length + "\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n";

            var writer = new DataWriter(socket.OutputStream);
            writer.WriteBytes(Encoding.UTF8.GetBytes(headers));
            writer.WriteBytes(payload);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        private const string PageStyle =
            "<style>" +
            "body{font-family:-apple-system,Segoe UI,Roboto,sans-serif;background:#121212;color:#eee;" +
            "margin:0;padding:24px;display:flex;justify-content:center;}" +
            ".card{max-width:640px;width:100%;}" +
            "h1{color:#1DB954;font-size:22px;}" +
            "textarea{width:100%;height:220px;background:#1e1e1e;color:#eee;border:1px solid #333;" +
            "border-radius:8px;padding:12px;font-family:monospace;font-size:13px;box-sizing:border-box;}" +
            "button{margin-top:16px;background:#1DB954;color:#000;border:0;border-radius:24px;" +
            "padding:14px 28px;font-size:16px;font-weight:600;width:100%;}" +
            "p{color:#b3b3b3;line-height:1.5;}" +
            "</style>";

        private static string BuildFormPage()
        {
            return "<!doctype html><html><head><meta name='viewport' " +
                   "content='width=device-width,initial-scale=1'><title>Sign in to Spotbox</title>" +
                   PageStyle + "</head><body><div class='card'>" +
                   "<h1>Sign in to Spotbox</h1>" +
                   "<p>Run the <b>LibreSpotUWP Login Helper</b> on a PC, sign in to Spotify there, " +
                   "and copy the sign-in details it produces. Paste them below and press Send - " +
                   "they go straight to your Xbox, so there is nothing to type on the console.</p>" +
                   "<form method='POST' action='/'>" +
                   "<textarea name='session' placeholder='Paste the sign-in details here' autofocus></textarea>" +
                   "<button type='submit'>Send to Xbox</button>" +
                   "</form></div></body></html>";
        }

        private static string BuildResultPage(bool success, string message)
        {
            return "<!doctype html><html><head><meta name='viewport' " +
                   "content='width=device-width,initial-scale=1'><title>Spotbox</title>" +
                   PageStyle + "</head><body><div class='card'>" +
                   "<h1>" + (success ? "Sent" : "Nothing sent") + "</h1>" +
                   "<p>" + message + "</p></div></body></html>";
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            Stop();
        }
    }
}
