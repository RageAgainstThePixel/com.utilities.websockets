using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Timers;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/", RequestHandler);
        app.Run();
    }

    private static async Task RequestHandler(HttpContext context)
    {
        try
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                Console.WriteLine($"[{DateTime.UtcNow}] Websocket Connection Established!");

                using var textTimer = new System.Timers.Timer(100);

                async void OnTextTimerOnElapsed(object? sender, ElapsedEventArgs e)
                {
                    await webSocket.SendAsync(new ArraySegment<byte>("Hello World!"u8.ToArray()), WebSocketMessageType.Text, true, CancellationToken.None);
                }

                textTimer.Elapsed += OnTextTimerOnElapsed;
                textTimer.Start();

                using var binaryTimer = new System.Timers.Timer(110);

                async void OnBinaryTimerOnElapsed(object? sender, ElapsedEventArgs e)
                {
                    await webSocket.SendAsync(new ArraySegment<byte>(RandomNumberGenerator.GetBytes(8)), WebSocketMessageType.Binary, true, CancellationToken.None);
                }

                binaryTimer.Elapsed += OnBinaryTimerOnElapsed;
                binaryTimer.Start();

                var buffer = new byte[1024 * 4];
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                while (!result.CloseStatus.HasValue)
                {
                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Text:
                            Console.WriteLine($"[{DateTime.UtcNow}] '{Encoding.UTF8.GetString(buffer, 0, result.Count)}'");
                            break;
                        case WebSocketMessageType.Binary:
                            Console.WriteLine($"[{DateTime.UtcNow}] {BitConverter.ToString(buffer, 0, result.Count).Replace("-", ", ")}");
                            break;
                        case WebSocketMessageType.Close:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    await webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                }

                binaryTimer.Stop();
                binaryTimer.Elapsed -= OnBinaryTimerOnElapsed;

                textTimer.Stop();
                textTimer.Elapsed -= OnTextTimerOnElapsed;

                await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
                Console.WriteLine($"[{DateTime.UtcNow}] Websocket Connection Closed!");
            }
            else
            {
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync("Hello World!");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(e);
        }
    }
}