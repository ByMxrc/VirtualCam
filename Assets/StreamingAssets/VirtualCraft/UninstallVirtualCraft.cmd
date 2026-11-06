@echo off
setlocal
set "FILTERDIR=%~dp0"
"%SystemRoot%\SysWOW64\regsvr32.exe" /s /u "%FILTERDIR%UnityCaptureFilter32.dll"
if errorlevel 1 exit /b 1
"%SystemRoot%\System32\regsvr32.exe" /s /u "%FILTERDIR%UnityCaptureFilter64.dll"
exit /b %errorlevel%
