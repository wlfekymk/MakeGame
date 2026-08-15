@echo off
cd /d D:\MakeGame
echo.
echo ==========================================
echo   MakeGame auto-push - hide console window
echo ==========================================
echo.
echo The scheduled task currently opens a console
echo window every 5 minutes. This re-registers it
echo to run completely hidden via a VBS launcher.
echo.
echo Nothing else changes - push behaviour is the same.
echo.
pause

echo.
echo --- removing old task ---
schtasks /delete /tn "MakeGame-AutoPush" /f
echo.
echo --- registering hidden task ---
schtasks /create /tn "MakeGame-AutoPush" /tr "wscript.exe //B //Nologo D:\MakeGame\push_silent.vbs" /sc minute /mo 5 /f
echo.
echo --- running once now (should show NO window) ---
wscript.exe //B //Nologo D:\MakeGame\push_silent.vbs
timeout /t 5 /nobreak >nul
echo.
echo --- result ---
type D:\MakeGame\push_result.log
echo.
echo ==========================================
echo   Done. No more console pop-ups.
echo   To stop auto-push entirely:
echo     schtasks /delete /tn "MakeGame-AutoPush" /f
echo ==========================================
echo.
pause
