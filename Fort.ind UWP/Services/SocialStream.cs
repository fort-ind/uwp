using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Web;

namespace Fort.ind_UWP
{
    public sealed class SocialStreamEventArgs : EventArgs
    {
        public SocialStreamEventArgs(string type, JsonObject body)
        {
            Type = type;
            Body = body;
        }

        public string Type { get; }

        public JsonObject Body { get; }
    }

    public sealed class SocialStream : IDisposable
    {
        private const string MainChannel = "main";

        private readonly string _channelId = Guid.NewGuid().ToString("N");

        private readonly object _lock = new object();

        private MessageWebSocket _socket;

        private bool _closed;

        public event EventHandler<SocialStreamEventArgs> MessageReceived;

        public event EventHandler Closed;

        public bool Unauthorized { get; private set; }

        public async Task<bool> ConnectAsync(string token, CancellationToken cancellationToken)
        {
            var socket = new MessageWebSocket();
            socket.Control.MessageType = SocketMessageType.Utf8;
            socket.SetRequestHeader("Authorization", "Bearer " + token);
            socket.MessageReceived += OnMessageReceived;
            socket.Closed += OnClosed;

            lock (_lock)
            {
                if (_closed)
                {
                    socket.Dispose();
                    return false;
                }
                _socket = socket;
            }

            try
            {
                await socket.ConnectAsync(new Uri($"wss://{MisskeyAuthService.InstanceHost}/streaming"))
                            .AsTask(cancellationToken);

                JsonObject body = new JsonObject();
                body.Add("channel", JsonValue.CreateStringValue(MainChannel));
                body.Add("id", JsonValue.CreateStringValue(_channelId));

                JsonObject message = new JsonObject();
                message.Add("type", JsonValue.CreateStringValue("connect"));
                message.Add("body", body);

                using (var writer = new DataWriter(socket.OutputStream))
                {
                    writer.UnicodeEncoding = UnicodeEncoding.Utf8;
                    writer.WriteString(message.Stringify());
                    await writer.StoreAsync().AsTask(cancellationToken);
                    writer.DetachStream();
                }

                return true;
            }
            catch (Exception ex)
            {
                var status = WebSocketError.GetStatus(ex.HResult);
                Unauthorized = status == WebErrorStatus.Unauthorized || status == WebErrorStatus.Forbidden;
                Debug.WriteLine($"SocialStream: connect failed - {status} {ex.GetType().Name}: {ex.Message}");
                Dispose();
                return false;
            }
        }

        private void OnMessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
        {
            string text;
            try
            {
                using (var reader = args.GetDataReader())
                {
                    reader.UnicodeEncoding = UnicodeEncoding.Utf8;
                    text = reader.ReadString(reader.UnconsumedBufferLength);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialStream: read failed - {WebSocketError.GetStatus(ex.HResult)} {ex.Message}");
                Dispose();
                return;
            }

            try
            {
                JsonObject message;
                if (!JsonObject.TryParse(text, out message)) return;
                if (!string.Equals(SocialJson.String(message, "type"), "channel", StringComparison.Ordinal)) return;

                var envelope = SocialJson.Object(message, "body");
                if (!string.Equals(SocialJson.String(envelope, "id"), _channelId, StringComparison.Ordinal)) return;

                var type = SocialJson.String(envelope, "type");
                if (string.IsNullOrEmpty(type)) return;

                MessageReceived?.Invoke(this, new SocialStreamEventArgs(type, SocialJson.Object(envelope, "body")));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialStream: message handling failed - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void OnClosed(IWebSocket sender, WebSocketClosedEventArgs args)
        {
            Debug.WriteLine($"SocialStream: closed by the server - {args.Code} {args.Reason}");
            Dispose();
        }

        public void Dispose()
        {
            MessageWebSocket socket;
            lock (_lock)
            {
                if (_closed) return;
                _closed = true;
                socket = _socket;
                _socket = null;
            }

            if (socket != null)
            {
                socket.MessageReceived -= OnMessageReceived;
                socket.Closed -= OnClosed;

                try
                {
                    socket.Close(1000, "");
                    socket.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialStream: close failed - {ex.Message}");
                }
            }

            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
