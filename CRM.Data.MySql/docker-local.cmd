@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0docker-local.ps1" %*
exit /b %ERRORLEVEL%
