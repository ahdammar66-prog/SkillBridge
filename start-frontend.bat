@echo off
cd /d "%~dp0client"
if not exist node_modules (
  echo Installing frontend packages...
  npm install
  if errorlevel 1 pause & exit /b 1
)
npm run dev
pause
