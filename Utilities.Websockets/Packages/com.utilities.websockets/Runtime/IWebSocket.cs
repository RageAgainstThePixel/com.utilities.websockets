// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
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
        event Action OnMessage;

        /// <summary>
        /// Occurs when the <see cref="IWebSocket"/> raises an error.
        /// </summary>
        event Action OnError;

        /// <summary>
        /// Occurs when the <see cref="IWebSocket"/> connection has been closed.
        /// </summary>
        event Action OnClose;

        Uri uri { get; }

        State State { get; }

        IReadOnlyDictionary<string, string> Headers { get; }

        IReadOnlyList<string> SubProtocols { get; }

        Task ConnectAsync();

        Task CloseAsync(CloseStatusCode code, string reason = "");

        Task SendAsync();

        Task ReceiveAsync();
    }
}
