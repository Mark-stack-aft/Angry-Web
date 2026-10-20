using System.Net;
using System.Text;
using System.Text.Json;

namespace AngryWeb.Service.Core;

public class BlockPageServer
{
    private readonly ILogger<BlockPageServer> _logger;
    private readonly ConfigManager _configManager;
    private readonly TemporaryAccessManager _accessManager;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public BlockPageServer(ILogger<BlockPageServer> logger, ConfigManager configManager, TemporaryAccessManager accessManager)
    {
        _logger = logger;
        _configManager = configManager;
        _accessManager = accessManager;
    }

    public void Start(int port)
    {
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add($"http://localhost:{port}/");

        try
        {
            _listener.Start();
            _logger.LogInformation("BlockPageServer started on port {Port}", port);
            Task.Run(() => ListenLoop(_cts.Token));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start BlockPageServer on port {Port}", port);
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context));
            }
            catch when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("BlockPage listener error: {Message}", ex.Message);
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;

        try
        {
            if (req.HttpMethod == "POST" && req.Url?.AbsolutePath == "/api/unlock")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var data = JsonSerializer.Deserialize<UnlockRequest>(body);

                if (data != null && IsPasswordValid(data.Password))
                {
                    // Allow for specified duration (e.g., 10 or 30 mins)
                    var minutes = data.Minutes > 0 && data.Minutes <= 120 ? data.Minutes : 15;
                    _accessManager.Allow(data.TargetUrl, TimeSpan.FromMinutes(minutes));
                    _logger.LogInformation("Temporarily allowed {Target} for {Mins} mins", data.TargetUrl, minutes);

                    var successJson = JsonSerializer.Serialize(new { success = true, redirect = data.TargetUrl });
                    await WriteResponse(res, successJson, "application/json");
                    return;
                }

                res.StatusCode = 401;
                var failJson = JsonSerializer.Serialize(new { success = false, message = "비밀번호가 올바르지 않습니다." });
                await WriteResponse(res, failJson, "application/json");
                return;
            }

            // Render Block Page
            var target = req.QueryString["target"] ?? "사이트";
            var pattern = req.QueryString["rule"] ?? "";
            var html = RenderBlockPageHtml(target, pattern);
            await WriteResponse(res, html, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling block page request");
            res.StatusCode = 500;
            res.Close();
        }
    }

    private bool IsPasswordValid(string? password)
    {
        var master = _configManager.CurrentConfig.MasterPasswordHash;
        return string.Equals(master, password, StringComparison.Ordinal);
    }

    private static async Task WriteResponse(HttpListenerResponse res, string content, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        res.ContentType = contentType;
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.OutputStream.Close();
    }

    private string RenderBlockPageHtml(string targetUrl, string matchedRule)
    {
        var bp = _configManager.CurrentConfig.BlockPage;
        return $$"""
<!DOCTYPE html>
<html lang="ko">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{{bp.Title}}</title>
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link href="https://fonts.googleapis.com/css2?family=Pretendard:wght@400;600;700;800&display=swap" rel="stylesheet">
    <style>
        :root {
            --bg-color: #0d1117;
            --card-bg: rgba(22, 27, 34, 0.85);
            --border-color: rgba(248, 81, 73, 0.3);
            --primary-accent: #f85149;
            --text-main: #f0f6fc;
            --text-muted: #8b949e;
            --input-bg: #090d13;
        }

        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
            font-family: 'Pretendard', -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
        }

        body {
            background-color: var(--bg-color);
            background-image: 
                radial-gradient(circle at 50% 20%, rgba(248, 81, 73, 0.15) 0%, transparent 50%),
                radial-gradient(circle at 80% 80%, rgba(139, 148, 158, 0.05) 0%, transparent 40%);
            color: var(--text-main);
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 24px;
        }

        .container {
            width: 100%;
            max-width: 580px;
            background: var(--card-bg);
            backdrop-filter: blur(16px);
            -webkit-backdrop-filter: blur(16px);
            border: 1px solid var(--border-color);
            border-radius: 20px;
            padding: 44px 36px;
            box-shadow: 0 20px 50px rgba(0, 0, 0, 0.6), 0 0 30px rgba(248, 81, 73, 0.1);
            text-align: center;
            animation: fadeIn 0.4s ease-out;
        }

        @keyframes fadeIn {
            from { opacity: 0; transform: translateY(12px) scale(0.98); }
            to { opacity: 1; transform: translateY(0) scale(1); }
        }

        .icon-badge {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 72px;
            height: 72px;
            background: rgba(248, 81, 73, 0.12);
            border: 1px solid rgba(248, 81, 73, 0.4);
            border-radius: 50%;
            margin-bottom: 24px;
            color: var(--primary-accent);
            box-shadow: 0 0 25px rgba(248, 81, 73, 0.25);
        }

        .icon-badge svg {
            width: 36px;
            height: 36px;
        }

        h1 {
            font-size: 26px;
            font-weight: 800;
            letter-spacing: -0.5px;
            margin-bottom: 12px;
            color: #fff;
        }

        p.subheading {
            font-size: 15px;
            color: var(--text-muted);
            margin-bottom: 24px;
            line-height: 1.5;
        }

        .url-box {
            background: var(--input-bg);
            border: 1px solid #30363d;
            border-radius: 12px;
            padding: 12px 16px;
            font-size: 13px;
            color: #58a6ff;
            word-break: break-all;
            margin-bottom: 24px;
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 8px;
        }

        .quote-box {
            border-left: 3px solid var(--primary-accent);
            background: rgba(255, 255, 255, 0.02);
            padding: 14px 18px;
            text-align: left;
            border-radius: 0 10px 10px 0;
            margin-bottom: 30px;
            font-size: 14px;
            color: #c9d1d9;
            font-style: italic;
        }

        .bypass-form {
            background: rgba(13, 17, 23, 0.7);
            border: 1px solid #30363d;
            border-radius: 14px;
            padding: 22px;
            text-align: left;
        }

        .form-title {
            font-size: 13px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: var(--text-muted);
            margin-bottom: 14px;
            display: flex;
            align-items: center;
            gap: 6px;
        }

        .form-row {
            display: flex;
            gap: 10px;
        }

        input[type="password"] {
            flex: 1;
            background: var(--input-bg);
            border: 1px solid #30363d;
            border-radius: 8px;
            padding: 12px 16px;
            font-size: 14px;
            color: var(--text-main);
            outline: none;
            transition: border-color 0.2s, box-shadow 0.2s;
        }

        input[type="password"]:focus {
            border-color: #58a6ff;
            box-shadow: 0 0 0 3px rgba(88, 166, 255, 0.2);
        }

        button.unlock-btn {
            background: #238636;
            color: #fff;
            border: none;
            border-radius: 8px;
            padding: 12px 20px;
            font-size: 14px;
            font-weight: 600;
            cursor: pointer;
            transition: background 0.2s, transform 0.1s;
            white-space: nowrap;
        }

        button.unlock-btn:hover {
            background: #2ea043;
        }

        button.unlock-btn:active {
            transform: scale(0.98);
        }

        .time-select {
            margin-top: 12px;
            display: flex;
            align-items: center;
            gap: 10px;
            font-size: 13px;
            color: var(--text-muted);
        }

        select {
            background: var(--input-bg);
            border: 1px solid #30363d;
            color: var(--text-main);
            padding: 6px 10px;
            border-radius: 6px;
            font-size: 13px;
            outline: none;
        }

        .status-msg {
            margin-top: 12px;
            font-size: 13px;
            min-height: 18px;
        }
        .status-error { color: #f85149; }
        .status-success { color: #3fb950; }
    </style>
</head>
<body>
    <div class="container">
        <div class="icon-badge">
            <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
                      d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"/>
            </svg>
        </div>
        <h1>{{bp.Heading}}</h1>
        <p class="subheading">{{bp.Message}}</p>

        <div class="url-box">
            <strong>차단 대상:</strong> <span>{{targetUrl}}</span>
        </div>

        <div class="quote-box">
            "{{bp.Quote}}"
        </div>

        <div class="bypass-form">
            <div class="form-title">
                <svg width="14" height="14" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"/>
                </svg>
                임시 허용 해제 (비밀번호 인증)
            </div>
            <div class="form-row">
                <input type="password" id="pwInput" placeholder="비밀번호 입력 (기본값: angryweb)" onkeypress="if(event.key==='Enter') doUnlock();">
                <button class="unlock-btn" onclick="doUnlock()">일시 해제</button>
            </div>
            <div class="time-select">
                <span>허용 유지 시간:</span>
                <select id="durationSelect">
                    <option value="5">5분</option>
                    <option value="15" selected>15분</option>
                    <option value="30">30분</option>
                    <option value="60">1시간</option>
                </select>
            </div>
            <div class="status-msg" id="statusMsg"></div>
        </div>
    </div>

    <script>
        async function doUnlock() {
            const pw = document.getElementById('pwInput').value;
            const minutes = parseInt(document.getElementById('durationSelect').value);
            const status = document.getElementById('statusMsg');
            const target = "{{targetUrl}}";

            if (!pw) {
                status.className = 'status-msg status-error';
                status.innerText = '비밀번호를 입력하세요.';
                return;
            }

            status.className = 'status-msg';
            status.innerText = '인증 중...';

            try {
                const res = await fetch('/api/unlock', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ password: pw, targetUrl: target, minutes: minutes })
                });

                const data = await res.json();
                if (data.success) {
                    status.className = 'status-msg status-success';
                    status.innerText = '인증 완료! 원래 페이지로 이동합니다...';
                    setTimeout(() => {
                        window.location.href = target.startsWith('http') ? target : ('https://' + target);
                    }, 1000);
                } else {
                    status.className = 'status-msg status-error';
                    status.innerText = data.message || '인증 실패';
                }
            } catch (err) {
                status.className = 'status-msg status-error';
                status.innerText = '서버 통신 오류가 발생했습니다.';
            }
        }
    </script>
</body>
</html>
""";
    }

    private class UnlockRequest
    {
        public string? Password { get; set; }
        public string TargetUrl { get; set; } = string.Empty;
        public int Minutes { get; set; } = 15;
    }
}
