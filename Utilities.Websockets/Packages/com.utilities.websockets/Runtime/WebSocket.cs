// Licensed under the MIT License. See LICENSE in the project root for license information.
#if !PLATFORM_WEBGL

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
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
            var protocol = uri.Scheme;

            if (!protocol.Equals("ws") && !protocol.Equals("wss"))
            {
                throw new ArgumentException($"Unsupported protocol: {protocol}");
            }

            Address = uri;
            SubProtocols = subProtocols ?? new List<string>();
            Headers = headers ?? new Dictionary<string, string>();
            _socket = new ClientWebSocket();
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
                lock (_lock)
                {
                    if (State == State.Open)
                    {
                        CloseAsync().Wait();
                    }

                    _socket?.Dispose();
                    _socket = null;

                    _lifetimeCts?.Cancel();
                    _lifetimeCts?.Dispose();
                    _lifetimeCts = null;

                    _semaphore?.Dispose();
                    _semaphore = null;
                }
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
        public event Action<CloseStatusCode, string> OnClose;

        /// <inheritdoc />
        public Uri Address { get; }

        /// <inheritdoc />
        public State State => _socket?.State switch
        {
            WebSocketState.Connecting => State.Connecting,
            WebSocketState.Open => State.Open,
            WebSocketState.CloseSent or WebSocketState.CloseReceived => State.Closing,
            _ => State.Closed
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
                if (State == State.Open)
                {
                    Debug.LogWarning("Websocket is already open!");
                    return;
                }

                _lifetimeCts?.Cancel();
                _lifetimeCts?.Dispose();
                _lifetimeCts = new CancellationTokenSource();
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
                        await CloseAsync(cancellationToken: CancellationToken.None);
                        break;
                    }
                }

                try
                {
                    await _semaphore.WaitAsync(CancellationToken.None);
                }
                finally
                {
                    _semaphore.Release();
                }

                await Awaiters.UnityMainThread;
            }
            catch (Exception e)
            {
                await Awaiters.UnityMainThread;

                switch (e)
                {
                    case TaskCanceledException:
                    case OperationCanceledException:
                        break;
                    default:
                        Debug.LogException(e);
                        OnError?.Invoke(e);
                        OnClose?.Invoke(CloseStatusCode.AbnormalClosure, e.Message);
                        break;
                }
            }
        }

        /// <inheritdoc />
        public async Task SendAsync(string text, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, cancellationToken);
                await _semaphore.WaitAsync(cts.Token).ConfigureAwait(false);

                if (State != State.Open)
                {
                    throw new InvalidOperationException("WebSocket is not ready.");
                }

                await _socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);
                await Awaiters.UnityMainThread;
            }
            catch (Exception e)
            {
                await Awaiters.UnityMainThread;

                switch (e)
                {
                    case TaskCanceledException:
                    case OperationCanceledException:
                        break;
                    default:
                        Debug.LogException(e);
                        OnError?.Invoke(e);
                        break;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <inheritdoc />
        public async Task SendAsync(ArraySegment<byte> data, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, cancellationToken);
                await _semaphore.WaitAsync(cts.Token).ConfigureAwait(false);

                if (State != State.Open)
                {
                    throw new InvalidOperationException("WebSocket is not ready.");
                }

                await _socket.SendAsync(data, WebSocketMessageType.Binary, true, cts.Token).ConfigureAwait(false);
                await Awaiters.UnityMainThread;
            }
            catch (Exception e)
            {
                await Awaiters.UnityMainThread;

                switch (e)
                {
                    case TaskCanceledException:
                    case OperationCanceledException:
                        break;
                    default:
                        Debug.LogException(e);
                        OnError?.Invoke(e);
                        break;
                }
            }
            finally
            {
                _semaphore.Release();
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
                if (State == State.Open)
                {
                    await _socket.CloseAsync((WebSocketCloseStatus)(int)code, reason, cancellationToken).ConfigureAwait(false);
                    await Awaiters.UnityMainThread;
                    OnClose?.Invoke(code, reason);
                }
            }
            catch (Exception e)
            {
                await Awaiters.UnityMainThread;

                switch (e)
                {
                    case TaskCanceledException:
                    case OperationCanceledException:
                        break;
                    default:
                        Debug.LogException(e);
                        OnError?.Invoke(e);
                        break;
                }
            }
        }
    }
}
#endif // !PLATFORM_WEBGL
