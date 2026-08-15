@echo off
cd /d D:\MakeGame
echo.
echo ==========================================
echo   MakeGame auto-push setup
echo ==========================================
echo.
echo Registers a Windows scheduled task that runs
echo D:\MakeGame\push.bat every 5 minutes.
echo Your PAT never leaves this PC.
echo.
echo --- checking git ---
git --version
echo.
echo --- registering task ---
schtasks /delete /tn "MakeGame-AutoPush" /f >nul 2>&1
schtasks /create /tn "MakeGame-AutoPush" /tr "cmd /c start \"\" /min D:\MakeGame\push.bat" /sc minute /mo 5 /f
echo.
echo --- running once now ---
call D:\MakeGame\push.bat
echo.
echo --- result ---
type D:\MakeGame\push_result.log
echo.
echo ==========================================
echo   Setup finished. Leave this window open
echo   long enough to read the result above.
echo ==========================================
echo.
pause
