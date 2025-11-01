// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using Unity.Collections;
using Utilities.Extensions;

namespace Utilities.WebSockets
{
    public readonly struct DataFrame : IDisposable
    {
        public OpCode Type { get; }

        public NativeArray<byte> Data { get; }

        public string Text
            => Type == OpCode.Text
                ? System.Text.Encoding.UTF8.GetString(Data.AsSpan())
                : string.Empty;

        public DataFrame(OpCode type, NativeArray<byte> data)
        {
            Type = type;
            Data = data;
        }

        public void Dispose()
            => Data.Dispose();
    }
}
