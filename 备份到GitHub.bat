@echo off
setlocal
cd /d "%~dp0"

echo.
echo ========== Backup to GitHub ==========

:: Set the remote URL to the target repository
git remote set-url origin https://github.com/Song-of-Bones/Lament-for-the-Nameless.git >nul 2>&1
if errorlevel 1 (
    git remote add origin https://github.com/Song-of-Bones/Lament-for-the-Nameless.git
)

:: Ask for a backup name; empty input falls back to "Auto backup"
set "NAME="
set /p NAME=Enter a name for this backup (empty = Auto backup):
set "NAME=%NAME:"=%"

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-ddTHH:mm:ss"') do set "DT=%%i"

git add -A
git diff --cached --quiet >nul 2>&1
if not errorlevel 1 goto :onlypush

echo Changes detected, committing...
if defined NAME (
    git -c user.name="XiaoXieChen-1" -c user.email="XiaoXieChen-1@users.noreply.github.com" commit -m "%NAME% - %DT%"
) else (
    git -c user.name="XiaoXieChen-1" -c user.email="XiaoXieChen-1@users.noreply.github.com" commit -m "Auto backup - %DT%"
)
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
