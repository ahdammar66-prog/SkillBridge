@echo off
cd /d "%~dp0server"
echo Starting SkillBridge API at http://localhost:5080
dotnet restore
if errorlevel 1 pause & exit /b 1
dotnet run
pause
