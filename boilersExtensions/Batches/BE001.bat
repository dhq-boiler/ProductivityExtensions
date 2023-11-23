@echo off
setlocal

chcp 65001

REM パラメーターの取得
set "devenvPath=%~1"
set "renameParent=%~2"
set "newSolutionName=%~3"
set "solutionPath=%~4"
set "additionalParams=%~5"
set "oldSolutionName=%~6"

echo devenvPath = %devenvPath%
echo renameParent = %renameParent%
echo newSolutionName = %newSolutionName%
echo solutionPath = %solutionPath%
echo additionalParams = %additionalParams%

REM パスの検証（オプショナル）
if not exist "%devenvPath%" (
    echo devenv.exeのパスが存在しません: %devenvPath%
    exit /b 1
)

REM 3秒待機
timeout /t 3 /nobreak >nul

set "currentDir=%solutionPath%"
for %%i in ("%currentDir%\..\..") do set "parentDir=%%~fi"
cd %parentDir%

REM 親フォルダのリネーム
if /I "%renameParent%"=="true" (
    ren %oldSolutionName% %newSolutionName%
)

REM Visual Studioの再起動
start "" "%devenvPath%" %additionalParams%

echo newSolutionPath = %newSolutionPath%

endlocal