@echo off
setlocal
cd /d D:\MakeGame

rem Block interactive credential prompt - without this git waits for input and the window never closes
set GIT_TERMINAL_PROMPT=0

echo === MakeGame push %DATE% %TIME% === > push_result.log
if exist .git\HEAD.lock del /f /q .git\HEAD.lock
if exist .git\index.lock del /f /q .git\index.lock

rem If nothing new, skip the network entirely and exit immediately
git rev-parse main > "%TEMP%\mg_local.txt" 2>nul
if exist .git\last_pushed.txt fc /b "%TEMP%\mg_local.txt" .git\last_pushed.txt >nul 2>&1 && (
  echo SKIP - no new commit since last push >> push_result.log
  goto :end
)

rem lowSpeed options make git abort after 20s of stall - prevents windows piling up forever
git -c credential.helper="store --file=D:/MakeGame/.git-credentials" -c http.lowSpeedLimit=1000 -c http.lowSpeedTime=20 push github main >> push_result.log 2>&1
echo EXIT=%ERRORLEVEL% >> push_result.log
if "%ERRORLEVEL%"=="0" copy /y "%TEMP%\mg_local.txt" .git\last_pushed.txt >nul 2>&1

:end
echo done %TIME% >> push_result.log
endlocal
exit /b 0
