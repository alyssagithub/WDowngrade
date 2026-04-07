@echo off
set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT_EXE=WDowngrade.exe
set REFS=System.dll,System.Core.dll,System.Net.Http.dll,System.Web.Extensions.dll,System.IO.Compression.dll,System.IO.Compression.FileSystem.dll

echo Building %OUT_EXE%...
"%CSC_PATH%" /target:exe /out:%OUT_EXE% Program.cs /r:%REFS%

if %ERRORLEVEL% equ 0 (
    echo Build successful. Running %OUT_EXE%...
    %OUT_EXE%
) else (
    echo Build failed.
    pause
)
