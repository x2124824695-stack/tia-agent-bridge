@echo off
REM ---------------------------------------------------------------------------
REM  TiaAgentBridge - grant the current Windows user Openness access.
REM
REM  TIA Portal only accepts Openness connections from members of the local
REM  group "Siemens TIA Openness".
REM
REM  Just DOUBLE-CLICK this file. It self-elevates via UAC.
REM  After it succeeds you MUST sign out of Windows and sign back in.
REM ---------------------------------------------------------------------------
setlocal
set "GRP=Siemens TIA Openness"
set "USERFQN=%COMPUTERNAME%\%USERNAME%"
set "LOG=%TEMP%\tia_grant_result.txt"

REM --- admin test: "fsutil dirty query" needs an elevated token and, unlike
REM --- "net session", does not depend on the Server service being started.
set "ADMIN=0"
fsutil dirty query %SystemDrive% >nul 2>&1
if %errorlevel% equ 0 set "ADMIN=1"

if "%ADMIN%"=="0" (
    echo Requesting administrator rights...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

> "%LOG%" echo TiaAgentBridge - Openness group grant
>>"%LOG%" echo time=%DATE% %TIME%
>>"%LOG%" echo user=%USERFQN%
>>"%LOG%" echo --- BEFORE ---
>>"%LOG%" net localgroup "%GRP%" 2>&1

echo.
echo ============================================================
echo  Group   : %GRP%
echo  Account : %USERFQN%
echo ============================================================

set "ADD_RC=na"
net localgroup "%GRP%" | findstr /i /c:"%USERNAME%" >nul 2>&1
if not errorlevel 1 goto already

echo.
echo Adding the account to the group...
net localgroup "%GRP%" "%USERFQN%" /add
set "ADD_RC=%errorlevel%"
goto report

:already
echo.
echo The account is already a member of the group - nothing to add.
set "ADD_RC=0"

:report
>>"%LOG%" echo add_exitcode=%ADD_RC%

echo.
echo --- Members of "%GRP%" AFTER ---
net localgroup "%GRP%"
>>"%LOG%" echo --- AFTER ---
>>"%LOG%" net localgroup "%GRP%" 2>&1

echo.
echo ============================================================
echo  DONE.  NEXT STEPS - both are mandatory:
echo    1. Close TIA Portal completely.
echo    2. Sign out of Windows, then sign back in.
echo       Lock / switch-user / restarting only TIA is NOT enough:
echo       Windows builds the group list into the token at logon.
echo ============================================================
echo.
echo Result logged to: %LOG%
echo.
echo Press any key to close this window.
pause >nul
endlocal
