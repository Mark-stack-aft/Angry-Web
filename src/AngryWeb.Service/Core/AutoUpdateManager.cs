using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;

namespace AngryWeb.Service.Core;

public class AutoUpdateManager
{
    private readonly ILogger<AutoUpdateManager> _logger;
    private readonly HttpClient _httpClient = new();
    private const string GitHubApiReleaseUrl = "https://api.github.com/repos/Mark-stack-aft/Angry-Web/releases/latest";

    public AutoUpdateManager(ILogger<AutoUpdateManager> logger)
    {
        _logger = logger;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AngryWeb-Service-Updater");
    }

    /// <summary>
    /// PC 부팅 후 인터넷 연결이 안정화될 때까지 대기합니다.
    /// </summary>
    public async Task WaitForInternetConnectionAsync(CancellationToken ct)
    {
        _logger.LogInformation("인터넷 연결 상태 확인 중...");
        while (!ct.IsCancellationRequested)
        {
            if (NetworkInterface.GetIsNetworkAvailable())
            {
                try
                {
                    // 1.1.1.1 또는 8.8.8.8로 실제 통신 가능 여부 체크
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                    if (reply.Status == IPStatus.Success)
                    {
                        _logger.LogInformation("인터넷 연결이 확인되었습니다.");
                        return;
                    }
                }
                catch
                {
                    // Ping 불가 환경 대비 HTTP HEAD 시도
                    try
                    {
                        using var res = await _httpClient.GetAsync("https://www.google.com", HttpCompletionOption.ResponseHeadersRead, ct);
                        if (res.IsSuccessStatusCode)
                        {
                            _logger.LogInformation("인터넷 연결이 확인되었습니다. (HTTP 통신 성공)");
                            return;
                        }
                    }
                    catch { }
                }
            }

            _logger.LogDebug("인터넷 대기 중... 5초 후 재시도");
            await Task.Delay(5000, ct);
        }
    }

    /// <summary>
    /// GitHub 최신 릴리스 바이너리가 있으면 다운로드 및 자동 교체(자가 업데이트)를 수행합니다.
    /// </summary>
    public async Task CheckAndApplyUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("GitHub에서 최신 릴리스 및 바이너리 업데이트 확인 중...");
            var res = await _httpClient.GetAsync(GitHubApiReleaseUrl, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogInformation("GitHub 릴리스가 아직 등록되지 않았거나 최신 상태입니다.");
                return;
            }

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("assets", out var assets) || assets.GetArrayLength() == 0)
                return;

            string? downloadUrl = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (string.Equals(name, "AngryWeb.Service.exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
                return;

            // 현재 실행 파일 경로
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExe))
                return;

            var currentDir = Path.GetDirectoryName(currentExe)!;
            var newExePath = Path.Combine(currentDir, "AngryWeb.Service.new.exe");
            var updateScriptPath = Path.Combine(currentDir, "update.bat");

            _logger.LogInformation("새로운 버전 바이너리 다운로드 중: {Url}", downloadUrl);
            var exeBytes = await _httpClient.GetByteArrayAsync(downloadUrl, ct);
            await File.WriteAllBytesAsync(newExePath, exeBytes, ct);

            _logger.LogInformation("바이너리 다운로드 완료. 서비스를 재시작하며 자가 업데이트를 완료합니다.");

            // 배치 스크립트로 안전하게 프로세스 교체 후 서비스 재시작
            var batchContent = $"""
@echo off
timeout /t 2 /nobreak > nul
copy /y "{newExePath}" "{currentExe}"
del "{newExePath}"
sc.exe start AngryWebService
del "%~f0"
""";
            await File.WriteAllTextAsync(updateScriptPath, batchContent, ct);

            // 업데이트 스크립트 실행 후 현재 프로세스 정상 종료 (서비스 중지)
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{updateScriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi);

            // 프로세스 종료 (Windows Service가 멈추고 배치파일이 교체 후 재시작함)
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("자가 업데이트 확인 중 오류 (기존 버전으로 계속 실행): {Msg}", ex.Message);
        }
    }
}
