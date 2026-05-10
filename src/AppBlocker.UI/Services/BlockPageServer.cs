using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AppBlocker.Core.Configuration;

namespace AppBlocker.UI.Services
{
    /// <summary>
    /// Локальный HTTP + HTTPS сервер.
    /// Перехватывает запросы к заблокированным сайтам и показывает страницу блокировки.
    /// HTTP — порт 80, HTTPS — порт 443 (с самоподписанным сертификатом).
    /// </summary>
    public class BlockPageServer : IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly Random _random = new Random();

        // HTTP (порт 80)
        private HttpListener _httpListener;
        private CancellationTokenSource _cts;
        private Task _httpTask;

        // HTTPS (порт 443) — через TcpListener + SslStream
        private TcpListener _httpsListener;
        private Task _httpsTask;
        private X509Certificate2 _selfSignedCert;

        private bool _isRunning;

        public BlockPageServer()
        {
            _configManager = new ConfigManager();
        }

        public void Start()
        {
            if (_isRunning) return;
            _cts = new CancellationTokenSource();

            // Генерируем самоподписанный сертификат
            _selfSignedCert = GenerateSelfSignedCert();

            // Запускаем HTTP-сервер (порт 80)
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add("http://+:80/");
                _httpListener.Start();
                _httpTask = Task.Run(() => HttpListenLoopAsync(_cts.Token));
                Debug.WriteLine("[BlockPageServer] HTTP запущен на порту 80");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BlockPageServer] HTTP не удалось запустить: {ex.Message}");
            }

            // Запускаем HTTPS-сервер (порт 443)
            try
            {
                _httpsListener = new TcpListener(IPAddress.Any, 443);
                _httpsListener.Start();
                _httpsTask = Task.Run(() => HttpsListenLoopAsync(_cts.Token));
                Debug.WriteLine("[BlockPageServer] HTTPS запущен на порту 443");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BlockPageServer] HTTPS не удалось запустить: {ex.Message}");
            }

            _isRunning = true;
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _cts?.Cancel();

            try { _httpListener?.Stop(); } catch { }
            try { _httpListener?.Close(); } catch { }
            try { _httpsListener?.Stop(); } catch { }

            try { _httpTask?.Wait(2000); } catch { }
            try { _httpsTask?.Wait(2000); } catch { }

            _httpListener = null;
            _httpsListener = null;
            _isRunning = false;
            Debug.WriteLine("[BlockPageServer] Остановлен");
        }

        // ===================== HTTP (порт 80) =====================

        private async Task HttpListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    _ = Task.Run(() => HandleHttpRequest(context));
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { }
            }
        }

        private void HandleHttpRequest(HttpListenerContext context)
        {
            try
            {
                string host = context.Request.Headers["Host"] ?? context.Request.Url?.Host ?? "сайт";
                string html = GenerateBlockPage(host);
                byte[] buffer = Encoding.UTF8.GetBytes(html);

                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.StatusCode = 200;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch { }
        }

        // ===================== HTTPS (порт 443) =====================

        private async Task HttpsListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await _httpsListener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleHttpsClient(client));
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { }
            }
        }

        private void HandleHttpsClient(TcpClient client)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;

                    using var sslStream = new SslStream(client.GetStream(), false);
                    sslStream.AuthenticateAsServer(_selfSignedCert, false, false);

                    // Читаем HTTP-запрос (нам нужен только Host заголовок)
                    byte[] requestBuffer = new byte[4096];
                    int bytesRead = sslStream.Read(requestBuffer, 0, requestBuffer.Length);
                    string requestText = Encoding.UTF8.GetString(requestBuffer, 0, bytesRead);

                    // Извлекаем Host из HTTP-заголовков
                    string host = "сайт";
                    foreach (var line in requestText.Split('\n'))
                    {
                        if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                        {
                            host = line.Substring(5).Trim().TrimEnd('\r');
                            // Убираем порт если есть
                            if (host.Contains(':')) host = host.Split(':')[0];
                            break;
                        }
                    }

                    string html = GenerateBlockPage(host);
                    byte[] body = Encoding.UTF8.GetBytes(html);

                    string responseHeader =
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: text/html; charset=utf-8\r\n" +
                        $"Content-Length: {body.Length}\r\n" +
                        "Connection: close\r\n" +
                        "\r\n";

                    byte[] headerBytes = Encoding.UTF8.GetBytes(responseHeader);
                    sslStream.Write(headerBytes);
                    sslStream.Write(body);
                    sslStream.Flush();
                }
            }
            catch { }
        }

        // ===================== Сертификат =====================

        private X509Certificate2 GenerateSelfSignedCert()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=AppBlocker Local CA",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // Добавляем расширения
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));

            var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(5));

            // Экспорт и реимпорт для совместимости с SslStream на Windows
            return new X509Certificate2(
                cert.Export(X509ContentType.Pfx, "appblocker"),
                "appblocker",
                X509KeyStorageFlags.MachineKeySet);
        }

        // ===================== HTML страница =====================

        private string GetTimeLeft()
        {
            try
            {
                var config = _configManager.LoadConfig();
                if (config.BlockEndTime.HasValue)
                {
                    var remaining = config.BlockEndTime.Value.ToUniversalTime() - DateTime.UtcNow;
                    if (remaining.TotalSeconds > 0)
                        return $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
                }
            }
            catch { }
            return null;
        }

        private string GenerateBlockPage(string host)
        {
            string timeLeft = GetTimeLeft();

            string timerBlock = timeLeft != null
                ? $@"<div class=""timer"">
                        <div class=""timer-label"">До конца сессии</div>
                        <div class=""timer-value"">{timeLeft}</div>
                     </div>"
                : "";

            return $@"<!DOCTYPE html>
<html lang=""ru"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Заблокировано — AppBlocker</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            background: #09090B;
            font-family: 'Segoe UI', system-ui, sans-serif;
            color: #FAFAFA;
            overflow: hidden;
        }}
        .container {{
            text-align: center;
            max-width: 600px;
            padding: 40px;
            animation: fadeIn 0.6s ease-out;
        }}
        @keyframes fadeIn {{
            from {{ opacity: 0; transform: translateY(20px); }}
            to {{ opacity: 1; transform: translateY(0); }}
        }}
        .shield {{
            font-size: 80px;
            margin-bottom: 20px;
            filter: drop-shadow(0 0 40px rgba(239, 68, 68, 0.3));
            animation: pulse 2s ease-in-out infinite;
        }}
        @keyframes pulse {{
            0%, 100% {{ transform: scale(1); }}
            50% {{ transform: scale(1.05); }}
        }}
        .title {{
            font-size: 28px;
            font-weight: 700;
            color: #F87171;
            margin-bottom: 8px;
        }}
        .domain {{
            font-size: 18px;
            color: #71717A;
            margin-bottom: 40px;
            word-break: break-all;
        }}
        .domain strong {{ color: #A1A1AA; }}
        .timer {{ margin-bottom: 40px; }}
        .timer-label {{
            font-size: 14px;
            color: #71717A;
            text-transform: uppercase;
            letter-spacing: 2px;
            margin-bottom: 8px;
        }}
        .timer-value {{
            font-size: 48px;
            font-weight: 700;
            color: #60A5FA;
            font-variant-numeric: tabular-nums;
            letter-spacing: 4px;
        }}
        .footer {{
            margin-top: 40px;
            font-size: 13px;
            color: #3F3F46;
        }}
        body::before {{
            content: '';
            position: fixed;
            top: -50%; left: -50%;
            width: 200%; height: 200%;
            background: radial-gradient(circle at 30% 40%, rgba(239, 68, 68, 0.05) 0%, transparent 50%),
                        radial-gradient(circle at 70% 60%, rgba(59, 130, 246, 0.05) 0%, transparent 50%);
            animation: bgMove 10s ease-in-out infinite alternate;
            z-index: -1;
        }}
        @keyframes bgMove {{
            0% {{ transform: translate(0, 0); }}
            100% {{ transform: translate(-5%, -5%); }}
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""shield"">🛡️</div>
        <div class=""title"">Сайт заблокирован</div>
        <div class=""domain"">Вы попытались открыть <strong>{WebUtility.HtmlEncode(host)}</strong></div>
        {timerBlock}
        <div class=""footer"">AppBlocker — Фокус и Продуктивность</div>
    </div>
</body>
</html>";
        }

        public void Dispose()
        {
            Stop();
            _selfSignedCert?.Dispose();
        }
    }
}
