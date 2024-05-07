// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Utilities.WebSockets
{
    public interface IWebSocket : IDisposable
    {
        /// <summary>
        /// Occurs when the <see cref="IWebSocket"/> connection has been established.
        /// </summary>
        event Action OnOpen;

        /// <summary>
        /// Occurs when the <see cref="IWebSocket"/> recieves a message.
        /// </summary>
        event Action<DataFrame> OnMessage;

        /// <summary>
        /// Occurs when the <see cref="IWebSocket"/> raises an error.
        /// </summary>
        event Action<Exception> OnError;

        /// <summary>
        /// Occurs when the <see cref="IWebSocket"/> connection has been closed.
        /// </summary>
        event Action<CloseStatusCode> OnClose;

        Uri Address { get; }

        State State { get; }

        IReadOnlyDictionary<string, string> Headers { get; }

        IReadOnlyList<string> SubProtocols { get; }

        void Connect();

        Task ConnectAsync(CancellationToken cancellationToken = default);

        Task SendAsync(ArraySegment<byte> data, CancellationToken cancellationToken = default);

        Task SendAsync(string text, CancellationToken cancellationToken = default);

        void Close();

        Task CloseAsync(CloseStatusCode code = CloseStatusCode.Normal, string reason = "", CancellationToken cancellationToken = default);
    }
}
