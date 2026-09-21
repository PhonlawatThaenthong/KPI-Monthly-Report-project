@echo off
REM ============================================================
REM  register-send-report-task.cmd
REM
REM  Registers the hourly Windows Task Scheduler job that actually
REM  sends the monthly KPI report mails. Without this task nothing
REM  is ever sent on schedule: the web app only has the manual
REM  "send now" button, by design (IIS recycles its app pool, so a
REM  timer hosted in the web app is not guaranteed to run).
REM
REM  Run this ONCE, from an elevated command prompt, as the same
REM  Windows account that has KPI_SMTP_PASSWORD set (task runs as
REM  the current user; user-level env vars are per account).
REM
REM  Undo:   schtasks /delete /tn "KPI Monthly Report - send" /f
REM  Status: schtasks /query  /tn "KPI Monthly Report - send" /v /fo list
REM  Test:   schtasks /run    /tn "KPI Monthly Report - send"
REM ============================================================

setlocal

set "TASK=KPI Monthly Report - send"
set "RUNNER=%~dp0send-report.cmd"

if not exist "%RUNNER%" (
    echo ERROR: not found: %RUNNER%
    exit /b 9
)

echo Registering task: %TASK%
echo Runs: hourly, starting at the next :05
echo Runner: %RUNNER%
echo.

schtasks /create /tn "%TASK%" /tr "\"%RUNNER%\"" /sc hourly /mo 1 /st 00:05 /rl LIMITED /f
if errorlevel 1 (
    echo.
    echo ERROR: schtasks failed. Run this from an elevated prompt.
    exit /b 1
)

echo.
echo Done. Verify with:
echo   schtasks /query /tn "%TASK%" /v /fo list
echo.
echo The task only sends to recipients whose day/hour has passed
echo and who have not already received this month's report.
echo Check results in the web UI: Report emails -^> log
echo or in tools\logs\send-report.log

endlocal
