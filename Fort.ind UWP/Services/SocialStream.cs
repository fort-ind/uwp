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

    public sealed class SocialNoteStreamEventArgs : EventArgs
    {
        public SocialNoteStreamEventArgs(string noteId, string type, JsonObject body)
        {
            NoteId = noteId;
            Type = type;
            Body = body;
        }

        public string NoteId { get; }

        public string Type { get; }

        public JsonObject Body { get; }
    }

    public sealed class SocialStream : IDisposable
    {
        public const string MainChannel = "main";

        private readonly string _channelId = Guid.NewGuid().ToString("N");

        private readonly string _channel;

        private readonly object _lock = new object();

        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);

        private MessageWebSocket _socket;

        private bool _closed;

        public SocialStream()
            : this(MainChannel)
        {
        }

        public SocialStream(string channel)
        {
            _channel = channel;
        }

        public event EventHandler<SocialStreamEventArgs> MessageReceived;

        public event EventHandler<SocialNoteStreamEventArgs> NoteUpdated;

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

                if (_channel != null)
                {
                    JsonObject body = new JsonObject();
                    body.Add("channel", JsonValue.CreateStringValue(_channel));
                    body.Add("id", JsonValue.CreateStringValue(_channelId));

                    await WriteAsync(socket, "connect", body, cancellationToken);
                }

                return true;
            }
            catch (Exception ex)
            {
                var status = WebSocketError.GetStatus(ex.HResult);
                Unauthorized = status == WebErrorStatus.Unauthorized || status == WebErrorStatus.Forbidden;
                AppLog.Error($"SocialStream: connect failed - {status}", ex);
                Dispose();
                return false;
            }
        }

        public async Task<bool> SendAsync(string type, JsonObject body)
        {
            MessageWebSocket socket;
            lock (_lock)
            {
                socket = _closed ? null : _socket;
            }

            if (socket == null) return false;

            try
            {
                await WriteAsync(socket, type, body, CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error($"SocialStream: send failed - {WebSocketError.GetStatus(ex.HResult)}", ex);
                Dispose();
                return false;
            }
        }

        private async Task WriteAsync(MessageWebSocket socket, string type, JsonObject body, CancellationToken cancellationToken)
        {
            JsonObject message = new JsonObject();
            message.Add("type", JsonValue.CreateStringValue(type));
            message.Add("body", body ?? new JsonObject());

            await _sendGate.WaitAsync(cancellationToken);
            try
            {
                using (var writer = new DataWriter(socket.OutputStream))
                {
                    writer.UnicodeEncoding = UnicodeEncoding.Utf8;
                    writer.WriteString(message.Stringify());
                    await writer.StoreAsync().AsTask(cancellationToken);
                    writer.DetachStream();
                }
            }
            finally
            {
                _sendGate.Release();
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
                AppLog.Error($"SocialStream: read failed - {WebSocketError.GetStatus(ex.HResult)}", ex);
                Dispose();
                return;
            }

            try
            {
                JsonObject message;
                if (!JsonObject.TryParse(text, out message)) return;

                var messageType = SocialJson.String(message, "type");
                if (string.Equals(messageType, "noteUpdated", StringComparison.Ordinal))
                {
                    var update = SocialJson.Object(message, "body");
                    var noteId = SocialJson.String(update, "id");
                    var updateType = SocialJson.String(update, "type");
                    if (!string.IsNullOrEmpty(noteId) && !string.IsNullOrEmpty(updateType))
                    {
                        NoteUpdated?.Invoke(this, new SocialNoteStreamEventArgs(noteId, updateType, SocialJson.Object(update, "body")));
                    }
                    return;
                }

                if (!string.Equals(messageType, "channel", StringComparison.Ordinal)) return;

                var envelope = SocialJson.Object(message, "body");
                if (!string.Equals(SocialJson.String(envelope, "id"), _channelId, StringComparison.Ordinal)) return;

                var type = SocialJson.String(envelope, "type");
                if (string.IsNullOrEmpty(type)) return;

                MessageReceived?.Invoke(this, new SocialStreamEventArgs(type, SocialJson.Object(envelope, "body")));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialStream: message handling failed", ex);
            }
        }

        private void OnClosed(IWebSocket sender, WebSocketClosedEventArgs args)
        {
            try
            {
                Debug.WriteLine($"SocialStream: closed by the server - {args.Code} {args.Reason}");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialStream: could not read why the socket closed", ex);
            }

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

            if (socket != null) Release(socket);

            try
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialStream: a close handler failed", ex);
            }
        }

        private void Release(MessageWebSocket socket)
        {
            try
            {
                socket.MessageReceived -= OnMessageReceived;
                socket.Closed -= OnClosed;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialStream: could not detach from the socket", ex);
            }

            try
            {
                socket.Close(1000, "");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialStream: close failed", ex);
            }

            try
            {
                socket.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialStream: dispose failed", ex);
            }
        }
    }
}
