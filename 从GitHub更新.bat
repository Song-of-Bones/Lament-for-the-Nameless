@echo off
setlocal
cd /d "%~dp0"

echo.
echo ========== Update from GitHub ==========

:: Set the remote URL to the target repository
git remote set-url origin https://github.com/Song-of-Bones/Lament-for-the-Nameless.git >nul 2>&1
if errorlevel 1 (
    git remote add origin https://github.com/Song-of-Bones/Lament-for-the-Nameless.git
)

echo Fetching latest changes...
git fetch origin main
if errorlevel 1 goto :fail

:: Check for uncommitted local changes
git diff --quiet >nul 2>&1
if errorlevel 1 goto :dirty
git diff --cached --quiet >nul 2>&1
if errorlevel 1 goto :dirty

:pull
echo Pulling latest code...
git merge --ff-only origin/main
if errorlevel 1 goto :conflict

echo.
echo Update done.
pause
exit /b 0

:dirty
echo.
echo WARNING: You have uncommitted local changes.
choice /C YN /M "Stash changes, update, then restore (Y) / Cancel (N)"
if errorlevel 2 goto :fail

git stash push -u -m "auto-stash before update"
if errorlevel 1 goto :fail

git merge --ff-only origin/main
if errorlevel 1 goto :conflict

echo Restoring stashed changes...
git stash pop
if errorlevel 1 (
    echo.
    echo Stashed changes could not auto-restore. Resolve conflicts manually, then run: git stash pop
    pause
    exit /b 1
)

echo.
echo Update done.
pause
exit /b 0

:conflict
echo.
echo Local history diverged from remote. Fast-forward failed.
echo If you want local to match remote EXACTLY (local changes will be LOST), run:
echo     git reset --hard origin/main
goto :fail

:fail
echo.
echo Update FAILED. See error messages above.
pause
exit /b 1
