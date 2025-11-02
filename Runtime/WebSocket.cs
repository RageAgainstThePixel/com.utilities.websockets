// Licensed under the MIT License. See LICENSE in the project root for license information.

#if !PLATFORM_WEBGL || UNITY_EDITOR

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using Utilities.Async;
using Utilities.Extensions;

namespace Utilities.WebSockets
{
    public class WebSocket : IWebSocket
    {
        public WebSocket(string url, IReadOnlyDictionary<string, string> requestHeaders = null, IReadOnlyList<string> subProtocols = null)
            : this(new Uri(url), requestHeaders, subProtocols)
        {
        }

        public WebSocket(Uri uri, IReadOnlyDictionary<string, string> requestHeaders = null, IReadOnlyList<string> subProtocols = null)
        {
            var protocol = uri.Scheme;

            if (!protocol.Equals("ws") && !protocol.Equals("wss"))
            {
                throw new ArgumentException($"Unsupported protocol: {protocol}");
            }

            Address = uri;
            RequestHeaders = requestHeaders ?? new Dictionary<string, string>();
            SubProtocols = subProtocols ?? new List<string>();
            _socket = new ClientWebSocket();
        }

        ~WebSocket() => Dispose(false);

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

        /// <inheritdoc />
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
        public IReadOnlyDictionary<string, string> RequestHeaders { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> SubProtocols { get; }

        /// <inheritdoc />
        public State State => _socket?.State switch
        {
            WebSocketState.Connecting => State.Connecting,
            WebSocketState.Open => State.Open,
            WebSocketState.CloseSent or WebSocketState.CloseReceived => State.Closing,
            _ => State.Closed
        };

        private readonly object _lock = new();
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

                foreach (var (key, value) in RequestHeaders)
                {
                    _socket.Options.SetRequestHeader(key, value);
                }

                for (var i = 0; i < SubProtocols.Count; i++)
                {
                    _socket.Options.AddSubProtocol(SubProtocols[i]);
                }

                await _socket.ConnectAsync(Address, cts.Token).ConfigureAwait(false);
                SyncContextUtility.RunOnUnityThread(() => OnOpen?.Invoke());

                const int bufferSize = 1024 * 16; // 16 KB
                var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                var receiveBuffer = buffer.AsMemory(0, bufferSize);

                try
                {
                    while (State == State.Open)
                    {
                        var messageLength = 0;
                        ValueWebSocketReceiveResult result;
                        using var stream = new MemoryStream(bufferSize);

                        do
                        {
                            cts.Token.ThrowIfCancellationRequested();
                            result = await _socket.ReceiveAsync(receiveBuffer, cts.Token).ConfigureAwait(false);
                            if (result.Count == 0) { continue; }
                            messageLength += result.Count;
                            stream.Write(receiveBuffer[..result.Count].Span);
                        } while (!result.EndOfMessage);

                        if (result.MessageType != WebSocketMessageType.Close)
                        {
                            var frame = new DataFrame((OpCode)(int)result.MessageType, stream.ToNativeArray(length: messageLength, allocator: Allocator.Persistent));

                            SyncContextUtility.RunOnUnityThread(() =>
                            {
                                try
                                {
                                    OnMessage?.Invoke(frame);
                                }
                                finally
                                {
                                    frame.Dispose();
                                }
                            });
                        }
                        else
                        {
                            await CloseAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
                            break;
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                try
                {
                    await _semaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception e)
            {
                switch (e)
                {
                    case TaskCanceledException:
                    case OperationCanceledException:
                        break;
                    default:
                        Debug.LogException(e);
                        SyncContextUtility.RunOnUnityThread(() => OnError?.Invoke(e));
                        SyncContextUtility.RunOnUnityThread(() => OnClose?.Invoke(CloseStatusCode.AbnormalClosure, e.Message));

                        break;
                }
            }
        }

        /// <inheritdoc />
        public async Task SendAsync(string text, CancellationToken cancellationToken = default)
            => await Internal_SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, cancellationToken);

        /// <inheritdoc />
        public async Task SendAsync(ArraySegment<byte> data, CancellationToken cancellationToken = default)
            => await Internal_SendAsync(data, WebSocketMessageType.Binary, cancellationToken);

        private async Task Internal_SendAsync(ArraySegment<byte> data, WebSocketMessageType opCode, CancellationToken cancellationToken)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, cancellationToken);
                await _semaphore.WaitAsync(cts.Token).ConfigureAwait(false);

                if (State != State.Open)
                {
                    throw new InvalidOperationException("WebSocket is not ready!");
                }

                await _socket.SendAsync(data, opCode, true, cts.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                switch (e)
                {
                    case TaskCanceledException:
                    case OperationCanceledException:
                        break;
                    default:
                        Debug.LogException(e);
                        SyncContextUtility.RunOnUnityThread(() => OnError?.Invoke(e));
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
                    SyncContextUtility.RunOnUnityThread(() => OnClose?.Invoke(code, reason));
                }
            }
            catch (Exception e)
            {
                switch (e)
                {
                    case TaskCanceledException:
                    case OperationCanceledException:
                        break;
                    default:
                        Debug.LogException(e);
                        SyncContextUtility.RunOnUnityThread(() => OnError?.Invoke(e));
                        break;
                }
            }
        }
    }
}
#endif // !PLATFORM_WEBGL || UNITY_EDITOR
