@echo off
cd /d D:\MakeGame
echo === MakeGame push %DATE% %TIME% === > push_result.log
if exist .git\HEAD.lock del /f /q .git\HEAD.lock
if exist .git\index.lock del /f /q .git\index.lock
if exist .git\_wtest del /f /q .git\_wtest
echo [1] locks cleared >> push_result.log
git --version >> push_result.log 2>&1
git remote set-url github https://github.com/wlfekymk/MakeGame.git >> push_result.log 2>&1
echo [2] local main: >> push_result.log
git rev-parse main >> push_result.log 2>&1
echo [3] pushing... >> push_result.log
git -c credential.helper="store --file=D:/MakeGame/.git-credentials" push github main >> push_result.log 2>&1
echo EXIT=%ERRORLEVEL% >> push_result.log
echo [4] VERIFY - actual remote main: >> push_result.log
git -c credential.helper="store --file=D:/MakeGame/.git-credentials" ls-remote github refs/heads/main >> push_result.log 2>&1
echo [5] VERIFY - remote branches: >> push_result.log
git -c credential.helper="store --file=D:/MakeGame/.git-credentials" ls-remote --heads github >> push_result.log 2>&1
echo [6] done >> push_result.log
