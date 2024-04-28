// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace Utilities.WebSockets
{
    public static class WebSocketExtensions
    {
        public static CloseStatusCode ParseCloseStatus(int closeCode)
            => Enum.IsDefined(typeof(CloseStatusCode), closeCode)
                ? (CloseStatusCode)closeCode
                : CloseStatusCode.Unknown;
    }
}
