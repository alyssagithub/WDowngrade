<# :
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Expression (Get-Content '%~f0' -Raw)"
exit /b
#>

# --- PURE POWERSHELL SCRIPT STARTS HERE ---
$ErrorActionPreference = 'Stop'

function Pause-Script {
    [Console]::ReadKey() | Out-Null
}

function Fatal-Error($msg) {
    Write-Host "$msg, contact the developer of WDowngrade" -ForegroundColor Red
    Pause-Script
    exit 1
}

try {
    $registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
    $downloadsID = '{374DE290-123F-4565-9164-39C4925E467B}'
    $downloadsRaw = (Get-ItemProperty $registryPath).$downloadsID
    $Downloads = [System.Environment]::ExpandEnvironmentVariables($downloadsRaw)
} catch {
    $Downloads = "$env:USERPROFILE\Downloads"
}
if (-not (Test-Path $Downloads)) {
    $Downloads = "$env:USERPROFILE\Downloads"
}

$SearchDirs = @($Downloads)

$browserPrefPaths = @(
    "$env:LOCALAPPDATA\Google\Chrome\User Data\Default\Preferences",
    "$env:LOCALAPPDATA\Google\Chrome\User Data\Profile 1\Preferences",
    "$env:LOCALAPPDATA\Google\Chrome\User Data\Profile 2\Preferences",
    "$env:LOCALAPPDATA\Microsoft\Edge\User Data\Default\Preferences",
    "$env:LOCALAPPDATA\Microsoft\Edge\User Data\Profile 1\Preferences",
    "$env:LOCALAPPDATA\BraveSoftware\Brave-Browser\User Data\Default\Preferences",
    "$env:APPDATA\Opera Software\Opera Stable\Preferences",
    "$env:APPDATA\Opera Software\Opera GX Stable\Preferences"
)
foreach ($pref in $browserPrefPaths) {
    if (Test-Path $pref) {
        try {
            $json = Get-Content $pref -Raw | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($json.download -and $json.download.default_directory) {
                $customDir = $json.download.default_directory
                if ((Test-Path $customDir) -and (-not ($SearchDirs -contains $customDir))) {
                    $SearchDirs += $customDir
                }
            }
        } catch {}
    }
}

$firefoxProfiles = "$env:APPDATA\Mozilla\Firefox\Profiles"
if (Test-Path $firefoxProfiles) {
    $profiles = Get-ChildItem -Path $firefoxProfiles -Directory -ErrorAction SilentlyContinue
    foreach ($profile in $profiles) {
        $prefsFile = Join-Path $profile.FullName "prefs.js"
        if (Test-Path $prefsFile) {
            try {
                $content = Get-Content $prefsFile -ErrorAction SilentlyContinue
                foreach ($line in $content) {
                    if ($line -match 'user_pref\("browser\.download\.dir", "(.*?)"\);') {
                        $customDir = $matches[1].Replace("\\", "\")
                        if ((Test-Path $customDir) -and (-not ($SearchDirs -contains $customDir))) {
                            $SearchDirs += $customDir
                        }
                    }
                }
            } catch {}
        }
    }
}

$VersionsDir = "$env:LOCALAPPDATA\Roblox\Versions"
$StartMenuDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Roblox"

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor 3072 -bor 12288
$urls = @(
    'https://whatexpsare.online/api/status/exploits',
    'http://farts.fadedis.xyz:25551/api/status/exploits'
)

$data = $null
foreach ($url in $urls) {
    if ($null -ne $data) { break }
    try {
        $data = Invoke-RestMethod -Uri $url -Headers @{'User-Agent'='WEAO-3PService'}
    } catch {
        try {
            $req = New-Object -ComObject Msxml2.ServerXMLHTTP.6.0
            $req.Open('GET', $url, $false)
            $req.setRequestHeader('User-Agent', 'WEAO-3PService')
            $req.Send()
            $data = $req.responseText | ConvertFrom-Json
        } catch {
            try {
                $json = curl.exe -s -S -L -H "User-Agent: WEAO-3PService" $url
                if ($json -is [array]) { $json = $json -join '' }
                if (-not [string]::IsNullOrWhiteSpace($json)) {
                    $data = $json | ConvertFrom-Json
                }
            } catch {}
        }
    }
}

if ($null -eq $data) {
    Fatal-Error "failed to get the list of executors"
}

$filtered = $data | Where-Object { $_.platform -eq 'Windows' -and $_.rbxversion -match '^version-' -and $_.extype -eq 'wexecutor' }
$executors = @($filtered)

Write-Host "`navailable executors:`n" -ForegroundColor Cyan
foreach ($ex in $executors) {
    Write-Host $($ex.title.Trim())
}

Write-Host ""
Write-Host "enter an executor name (case-insensitive): " -NoNewline -ForegroundColor Cyan
$choice = Read-Host
$rbxversion = $null
$executorName = $null

foreach ($ex in $executors) {
    if ($ex.title -match "(?i)$choice") {
        $rbxversion = $ex.rbxversion
        $executorName = $ex.title.Trim()
        break
    }
}

if (-not $rbxversion) {
    Fatal-Error "invalid selection"
}

$encExploit = [uri]::EscapeDataString($executorName)
$Url = "https://rdd.whatexpsare.online/?channel=LIVE&binaryType=WindowsPlayer&parallelDownloads=true&exploit=$encExploit"
$TargetZipPattern = "WEAO-LIVE-WindowsPlayer-$rbxversion*.zip"
$RequiresDownload = $true

$foundZips = @()
foreach ($dir in $SearchDirs) {
    $foundZips += Get-ChildItem -Path $dir -Filter $TargetZipPattern -ErrorAction SilentlyContinue
}
$foundZips = $foundZips | Sort-Object LastWriteTime -Descending

if ($foundZips.Count -gt 0) {
    $AbsoluteZip = $foundZips[0].FullName
    $Downloads = [System.IO.Path]::GetDirectoryName($AbsoluteZip)
    $RequiresDownload = $false
}

$RetryCount = 0
while ($true) {
    if ($RequiresDownload) {
        Write-Host "downloading from your browser, please do not close the rdd tab until it's done" -ForegroundColor Cyan
        Start-Process $Url
        
        Write-Host "`nif the zip file is downloaded but the 'download complete' message doesn't appear, then press 'm' to manually select the zip file`n" -ForegroundColor Yellow
        
        $AbsoluteZip = $null
        while ($true) {
            $files = @()
            foreach ($dir in $SearchDirs) {
                $files += Get-ChildItem -Path $dir -Filter $TargetZipPattern -ErrorAction SilentlyContinue
            }
            $files = $files | Sort-Object LastWriteTime -Descending
            
            if ($files.Count -gt 0) {
                $AbsoluteZip = $files[0].FullName
                $Downloads = [System.IO.Path]::GetDirectoryName($AbsoluteZip)
                break
            }
            if ([console]::KeyAvailable) {
                $k = [console]::ReadKey($true)
                if ($k.Key.ToString() -eq 'M') {
                    Add-Type -AssemblyName System.Windows.Forms
                    $ofd = New-Object System.Windows.Forms.OpenFileDialog
                    $ofd.Filter = 'ZIP Files (*.zip)|*.zip'
                    $ofd.Title = 'Select Roblox ZIP'
                    if ($ofd.ShowDialog() -eq 'OK') {
                        $AbsoluteZip = $ofd.FileName
                        $Downloads = [System.IO.Path]::GetDirectoryName($AbsoluteZip)
                        break
                    }
                }
            }
            Start-Sleep -Seconds 1
        }
        
        if (-not $AbsoluteZip) {
            Fatal-Error "failed to locate zip file"
        }
    }
    
    # Wait for download/release
    while ($true) {
        Start-Sleep -Seconds 2
        try {
            $stream = [System.IO.File]::Open($AbsoluteZip, 'Open', 'Read', 'None')
            $stream.Close()
            break 
        } catch { }
    }
    Write-Host "download complete, extracting..." -ForegroundColor White
    
    $FolderName = $rbxversion
    $OutDir = Join-Path $VersionsDir $FolderName
    
    Get-Process RobloxPlayerBeta -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path $OutDir)) {
        New-Item -ItemType Directory -Path $OutDir | Out-Null
    }
    
    $extractSuccess = $false
    try {
        $process = Start-Process -FilePath "tar.exe" -ArgumentList "-xf `"$AbsoluteZip`" -C `"$OutDir`"" -Wait -NoNewWindow -PassThru -ErrorAction SilentlyContinue
        if ($process.ExitCode -eq 0) { $extractSuccess = $true }
    } catch {}
    
    if (-not $extractSuccess) {
        try {
            Expand-Archive -LiteralPath $AbsoluteZip -DestinationPath $OutDir -Force -ErrorAction Stop
            $extractSuccess = $true
        } catch {}
    }
    
    if (-not $extractSuccess) {
        $winrarPath = ""
        if (Test-Path "${env:ProgramFiles}\WinRAR\WinRAR.exe") {
            $winrarPath = "${env:ProgramFiles}\WinRAR\WinRAR.exe"
        } elseif (Test-Path "${env:ProgramFiles(x86)}\WinRAR\WinRAR.exe") {
            $winrarPath = "${env:ProgramFiles(x86)}\WinRAR\WinRAR.exe"
        }
        if ($winrarPath) {
            try {
                $process = Start-Process -FilePath $winrarPath -ArgumentList "x -ibck -o+ -y `"$AbsoluteZip`" * `"$OutDir\`"" -Wait -NoNewWindow -PassThru -ErrorAction SilentlyContinue
                if ($process.ExitCode -eq 0) { $extractSuccess = $true }
            } catch {}
        }
    }
    
    if (-not $extractSuccess) {
        if ($RetryCount -ge 3) {
            Fatal-Error "all extraction methods failed after multiple retries"
        }
        $RetryCount++
        Remove-Item $AbsoluteZip -Force -ErrorAction SilentlyContinue
        $RequiresDownload = $true
        continue
    }
    
    break
}

Remove-Item $AbsoluteZip -Force -ErrorAction SilentlyContinue

if (-not (Test-Path $StartMenuDir)) {
    New-Item -ItemType Directory -Path $StartMenuDir | Out-Null
}

$ExePath = Join-Path $OutDir "RobloxPlayerBeta.exe"
$LnkPath = Join-Path $StartMenuDir "Roblox Player.lnk"

$ws = New-Object -ComObject WScript.Shell
$s = $ws.CreateShortcut($LnkPath)
$s.TargetPath = $ExePath
$s.Save()

$regPath = "HKCU:\Software\Classes\roblox-player\shell\open\command"
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
}
Set-Item -Path $regPath -Value "`"$ExePath`" %1" -Force
Set-ItemProperty -Path $regPath -Name "version" -Value $FolderName -Force

$allOldDirs = Get-ChildItem -Path $VersionsDir -Directory -ErrorAction SilentlyContinue
foreach ($dir in $allOldDirs) {
    if ($dir.Name -ne $FolderName -and $dir.Name -match '^version-') {
        $studioExe = Join-Path $dir.FullName "RobloxStudioBeta.exe"
        if (-not (Test-Path $studioExe)) {
            Remove-Item $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host "done, open roblox to verify it worked" -ForegroundColor Cyan

$autoUpdated = $false
while ($true) {
    Start-Sleep -Seconds 1
    
    if (Get-Process "RobloxPlayerInstaller", "RobloxPlayerLauncher" -ErrorAction SilentlyContinue) {
        $autoUpdated = $true
    }

    $roblox = Get-Process RobloxPlayerBeta -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($roblox -and $roblox.Path) {
        if ($roblox.Path -match [regex]::Escape($FolderName)) {
            Write-Host "worked, you can close this window now" -ForegroundColor Green
        } else {
            if ($autoUpdated) {
                Write-Host "roblox automatically updated itself back to a newer version, you'll have to install fishstrap at https://fishstrap.app, and use that to downgrade roblox to $FolderName" -ForegroundColor Red
            } else {
                Fatal-Error "something went wrong, the version is incorrect"
            }
        }
        break
    }
}

Pause-Script