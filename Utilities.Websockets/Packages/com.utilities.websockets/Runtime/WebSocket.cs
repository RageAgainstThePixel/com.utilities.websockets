// Licensed under the MIT License. See LICENSE in the project root for license information.
#if !PLATFORM_WEBGL

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utilities.Async;

namespace Utilities.WebSockets
{
    public class WebSocket : IWebSocket
    {
        public WebSocket(string url, IReadOnlyList<string> subProtocols = null, IReadOnlyDictionary<string, string> headers = null)
            : this(new Uri(url), subProtocols, headers)
        {
        }

        public WebSocket(Uri uri, IReadOnlyList<string> subProtocols = null, IReadOnlyDictionary<string, string> headers = null)
        {
            Address = uri;
            SubProtocols = subProtocols ?? new List<string>();
            Headers = headers ?? new Dictionary<string, string>();
            _lifetimeCts = new CancellationTokenSource();
        }

        ~WebSocket()
        {
            Dispose(false);
        }

        #region IDisposable

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _lifetimeCts?.Cancel();
                _lifetimeCts?.Dispose();
                _lifetimeCts = null;

                _semaphore?.Dispose();
                _semaphore = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion IDisposable

        /// <inheritdoc />
        public event Action OnOpen;

        /// <inheritdoc />
        public event Action<DataFrame> OnMessage;

        /// <inheritdoc />
        public event Action<Exception> OnError;

        /// <inheritdoc />
        public event Action<CloseStatusCode> OnClose;

        /// <inheritdoc />
        public Uri Address { get; }

        /// <inheritdoc />
        public State State => _socket?.State switch
        {
            WebSocketState.Connecting => State.Connecting,
            WebSocketState.Open => State.Open,
            WebSocketState.CloseSent or WebSocketState.CloseReceived => State.Closing,
            _ => State.Closed,
        };

        /// <inheritdoc />
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> SubProtocols { get; }

        private object _lock = new();
        private ClientWebSocket _socket;
        private SemaphoreSlim _semaphore = new(1, 1);
        private CancellationTokenSource _lifetimeCts;

        /// <inheritdoc />
        public async void Connect()
            => await ConnectAsync();

        /// <inheritdoc />
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                lock (_lock)
                {
                    if (_socket != null)
                    {
                        // already connected
                        return;
                    }

                    _socket = new ClientWebSocket();
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, cancellationToken);

                foreach (var header in Headers)
                {
                    _socket.Options.SetRequestHeader(header.Key, header.Value);
                }

                foreach (var subProtocol in SubProtocols)
                {
                    _socket.Options.AddSubProtocol(subProtocol);
                }

                await _socket.ConnectAsync(Address, cts.Token);
                await Awaiters.UnityMainThread;
                OnOpen?.Invoke();

                var buffer = new Memory<byte>(new byte[8192]);

                while (State == State.Open)
                {
                    ValueWebSocketReceiveResult result;
                    using var stream = new MemoryStream();

                    do
                    {
                        result = await _socket.ReceiveAsync(buffer, cts.Token).ConfigureAwait(false);
                        stream.Write(buffer.Span[..result.Count]);
                    } while (!result.EndOfMessage);

                    await stream.FlushAsync(cts.Token).ConfigureAwait(false);
                    var memory = new ReadOnlyMemory<byte>(stream.GetBuffer(), 0, (int)stream.Length);

                    if (result.MessageType != WebSocketMessageType.Close)
                    {
                        await Awaiters.UnityMainThread;
                        OnMessage?.Invoke(new DataFrame((OpCode)(int)result.MessageType, memory));
                    }
                    else
                    {
                        await CloseAsync(cancellationToken: cts.Token);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                await Awaiters.UnityMainThread;
                OnError?.Invoke(e);
                OnClose?.Invoke(CloseStatusCode.AbnormalClosure);
            }
            finally
            {
                lock (_lock)
                {
                    _socket?.Dispose();
                    _socket = null;
                }
            }
        }

        /// <inheritdoc />
        public async Task SendAsync(string text, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (State != State.Open) { return; }

                await _socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellationToken);
            }
            catch (Exception e)
            {
                await Awaiters.UnityMainThread;
                OnError?.Invoke(e);
            }
            finally
            {
                _semaphore?.Release();
            }
        }

        /// <inheritdoc />
        public async Task SendAsync(ArraySegment<byte> data, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (State != State.Open) { return; }

                await _socket.SendAsync(data, WebSocketMessageType.Binary, true, cancellationToken);
            }
            catch (Exception e)
            {
                await Awaiters.UnityMainThread;
                OnError?.Invoke(e);
            }
            finally
            {
                _semaphore?.Release();
            }
        }

        /// <inheritdoc />
        public async void Close()
            => await CloseAsync();

        /// <inheritdoc />
        public async Task CloseAsync(CloseStatusCode code = CloseStatusCode.Normal, string reason = "", CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (State != State.Open) { return; }

                await _socket.CloseAsync((WebSocketCloseStatus)(int)code, reason, cancellationToken);
                await Awaiters.UnityMainThread;
                OnClose?.Invoke(code);
            }
            catch (Exception e)
            {
                await Awaiters.UnityMainThread;
                OnError?.Invoke(e);
            }
            finally
            {
                _semaphore?.Release();
            }
        }
    }
}
#endif // !PLATFORM_WEBGL
