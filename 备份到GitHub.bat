@echo off
setlocal
cd /d "%~dp0"

echo.
echo ========== Backup to GitHub ==========

git add -A
git diff --cached --quiet >nul 2>&1
if not errorlevel 1 goto :onlypush

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-ddTHH:mm:ss"') do set "DT=%%i"
echo Changes detected, committing...
git -c user.name="XiaoXieChen-1" -c user.email="XiaoXieChen-1@users.noreply.github.com" commit -m "Auto backup - %DT%"
if errorlevel 1 goto :fail

:onlypush
echo Pushing to GitHub...
git push origin main
if errorlevel 1 goto :fail

echo.
echo Backup done.
pause
exit /b 0

:fail
echo.
echo Backup FAILED. See error messages above.
pause
exit /b 1
