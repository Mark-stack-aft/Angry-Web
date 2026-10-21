# Angry Web 서비스 관리 스크립트 (관리자 권한 필요)
# 사용법: PowerShell을 관리자 권한으로 실행 후 실행

param (
    [Parameter(Mandatory=$true)]
    [ValidateSet("install", "start", "stop", "uninstall", "status")]
    [string]$Action,
    [securestring]$Password = (New-Object System.Security.SecureString)
)

$ServiceName = "AngryWebService"
$BinPath = Join-Path $PSScriptRoot "src\AngryWeb.Service\bin\Release\net10.0\win-x64\publish\AngryWeb.Service.exe"

# If publish not yet run, point to debug binary
if (-not (Test-Path $BinPath)) {
    $BinPath = Join-Path $PSScriptRoot "src\AngryWeb.Service\bin\Debug\net10.0\AngryWeb.Service.exe"
}

function Test-Admin {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Error "이 작업은 반드시 관리자 권한으로 실행해야 합니다. PowerShell을 관리자로 실행해주세요."
        exit 1
    }
}

function Test-PasswordVerification {
    param([securestring]$InputPw)
    # Default master password
    $Expected = "angryweb"
    
    # Try reading from config if exists
    $ConfigPath = "$env:ProgramData\AngryWeb\config.json"
    if (Test-Path $ConfigPath) {
        try {
            $json = Get-Content $ConfigPath -Raw | ConvertFrom-Json
            if ($json.MasterPasswordHash) {
                $Expected = $json.MasterPasswordHash
            }
        } catch {}
    }

    $PlainPw = ""
    if ($InputPw -and $InputPw.Length -gt 0) {
        $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($InputPw)
        $PlainPw = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
    }

    if ($PlainPw -ne $Expected) {
        $entered = Read-Host "보안을 위해 관리자 마스터 비밀번호를 입력하세요" -AsSecureString
        $BSTR2 = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($entered)
        $PlainPw = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR2)
        if ($PlainPw -ne $Expected) {
            Write-Error "비밀번호가 올바르지 않습니다. 작업이 거부되었습니다."
            exit 1
        }
    }
}

Test-Admin

switch ($Action) {
    "install" {
        Write-Host "[1/2] 빌드 및 실행 바이너리 확인 중..." -ForegroundColor Cyan
        if (-not (Test-Path $BinPath)) {
            Write-Host "바이너리가 없습니다. dotnet build를 실행합니다..."
            dotnet build (Join-Path $PSScriptRoot "src\AngryWeb.Service\AngryWeb.Service.csproj")
        }

        Write-Host "[2/2] Windows Service 등록 중 ($ServiceName)..." -ForegroundColor Cyan
        sc.exe create $ServiceName binPath= "`"$BinPath`"" start= auto DisplayName= "Angry Web Filter Service"
        sc.exe description $ServiceName "웹 사이트 차단 및 집중 유지를 위한 Angry Web 보안 필터링 서비스"
        sc.exe failure $ServiceName reset= 0 actions= restart/60000/restart/60000/restart/60000
        
        Write-Host "서비스 시작 중..." -ForegroundColor Green
        sc.exe start $ServiceName
        Write-Host "Angry Web이 성공적으로 설치 및 구동되었습니다!" -ForegroundColor Green
    }

    "start" {
        sc.exe start $ServiceName
    }

    "stop" {
        Write-Warning "임의 종료를 방지하기 위해 마스터 비밀번호를 검증합니다."
        Test-PasswordVerification -InputPw $Password
        sc.exe stop $ServiceName
        Write-Host "서비스가 정지되었습니다." -ForegroundColor Yellow
    }

    "uninstall" {
        Write-Warning "제거를 방지하기 위해 마스터 비밀번호를 검증합니다."
        Test-PasswordVerification -InputPw $Password
        sc.exe stop $ServiceName
        sc.exe delete $ServiceName
        Write-Host "Angry Web 서비스가 삭제되었습니다." -ForegroundColor Green
    }

    "status" {
        sc.exe query $ServiceName
    }
}
