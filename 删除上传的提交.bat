@echo off
setlocal
cd /d "%~dp0"

echo.
echo ===== Squash away ONE bad commit on GitHub (keep the renamed one) =====

set "GOOD="
set /p GOOD=Enter the hash of the GOOD (renamed) commit to KEEP:
set "GOOD=%GOOD:"=%"
if not defined GOOD goto :fail_empty

set "BAD="
set /p BAD=Enter the hash of the BAD commit to squash away (must be directly BELOW the good one):
set "BAD=%BAD:"=%"
if not defined BAD goto :fail_empty

echo Fetching remote info...
git fetch origin main
if errorlevel 1 goto :fail

git cat-file -e "%GOOD%^{commit}" 2>nul
if errorlevel 1 (
    echo.
    echo Commit not found: %GOOD%
    goto :fail
)
git cat-file -e "%BAD%^{commit}" 2>nul
if errorlevel 1 (
    echo.
    echo Commit not found: %BAD%
    goto :fail
)

echo.
echo KEEP:
git log --oneline -1 "%GOOD%"
echo SQUASH AWAY:
git log --oneline -1 "%BAD%"

:: BAD must be the direct parent of GOOD (adjacent commits only)
set "PARENT="
set "BADFULL="
set "GOODFULL="
set "REMOTE="
for /f %%i in ('git rev-parse "%GOOD%~1"') do set "PARENT=%%i"
for /f %%i in ('git rev-parse "%BAD%"') do set "BADFULL=%%i"
for /f %%i in ('git rev-parse "%GOOD%"') do set "GOODFULL=%%i"
for /f %%i in ('git rev-parse origin/main') do set "REMOTE=%%i"

if not "%PARENT%" == "%BADFULL%" (
    echo.
    echo ERROR: %BAD% is not the direct parent of %GOOD%.
    echo This tool only squashes two ADJACENT commits.
    goto :fail
)
if not "%REMOTE%" == "%GOODFULL%" (
    echo.
    echo ERROR: %GOOD% is NOT the latest commit on GitHub.
    goto :fail
)

echo.
echo After squash, GitHub main becomes ONE commit with the same files:
git log --oneline -1 "%GOOD%"
echo ...sitting directly on top of:
git log --oneline -1 "%BAD%~1"
echo.
choice /C YN /M "Squash and force-push to GitHub? (Y=continue, N=cancel)"
if errorlevel 2 goto :cancel

:: Subject of the GOOD commit (captured via temp files; for /f re-parses %s and breaks it)
set "MSG="
git log -1 --format="%%s" "%GOODFULL%" > "%TEMP%\git_squash_msg.txt"
if errorlevel 1 goto :fail
set /p MSG=<"%TEMP%\git_squash_msg.txt"
del "%TEMP%\git_squash_msg.txt" >nul 2>&1
if not defined MSG goto :fail

:: Tree of the GOOD commit
set "GOODTREE="
git rev-parse "%GOODFULL%^{tree}" > "%TEMP%\git_squash_tree.txt"
if errorlevel 1 goto :fail
set /p GOODTREE=<"%TEMP%\git_squash_tree.txt"
del "%TEMP%\git_squash_tree.txt" >nul 2>&1
if not defined GOODTREE goto :fail

:: Build the squashed commit (author identity same as Backup script)
git -c user.name="XiaoXieChen-1" -c user.email="XiaoXieChen-1@users.noreply.github.com" commit-tree "%GOODTREE%" -p "%BAD%~1" -m "%MSG%" > "%TEMP%\git_squash_new.txt"
if errorlevel 1 goto :fail
set "NEW="
set /p NEW=<"%TEMP%\git_squash_new.txt"
del "%TEMP%\git_squash_new.txt" >nul 2>&1
if not defined NEW goto :fail

git push --force origin "%NEW%:main"
if errorlevel 1 goto :fail

git reset --hard "%NEW%"
if errorlevel 1 goto :fail

echo.
echo Done. The BAD commit is gone from GitHub. File contents are unchanged.
echo History now:
git log --oneline -3
pause
exit /b 0

:cancel
echo.
echo Cancelled. Nothing was changed.
pause
exit /b 0

:fail_empty
echo.
echo Missing input.
goto :fail

:fail
echo.
echo Operation FAILED.
pause
exit /b 1
