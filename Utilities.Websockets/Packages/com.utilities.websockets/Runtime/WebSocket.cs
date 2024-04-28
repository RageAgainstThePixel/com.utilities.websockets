// Licensed under the MIT License. See LICENSE in the project root for license information.
#if !PLATFORM_WEBGL

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Utilities.WebSockets
{
    public class WebSocket : IWebSocket
    {
        public event Action OnOpen;
        public event Action OnMessage;
        public event Action OnError;
        public event Action OnClose;

        public Uri uri { get; }

        public State State { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public IReadOnlyList<string> SubProtocols { get; }

        public Task ConnectAsync()
        {
            OnOpen?.Invoke();
            throw new NotImplementedException();
        }

        public Task CloseAsync(CloseStatusCode code, string reason = "")
        {
            OnClose?.Invoke();
            throw new NotImplementedException();
        }

        public Task SendAsync()
        {
            throw new NotImplementedException();
        }

        public Task ReceiveAsync()
        {
            OnMessage?.Invoke();
            OnError?.Invoke();
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
#endif // !PLATFORM_WEBGL
