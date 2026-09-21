@echo off
REM ============================================================
REM  send-report.cmd  -  hourly wrapper for KpiReport.Etl send-report
REM
REM  Windows Task Scheduler runs THIS file, not the exe directly:
REM    * it cd's to the exe folder first, so App.config and the
REM      relative mock-data path resolve the same way as when you
REM      run it by hand
REM    * it appends stdout/stderr to logs\send-report.log with a
REM      timestamp, so a failed run leaves evidence behind
REM    * it returns the exe's exit code (= number of failed mails)
REM      so the task's "Last Run Result" is meaningful
REM
REM  Schedule: every hour. Per-recipient day/hour lives in the
REM  database, the exe decides whose turn it is on each run.
REM ============================================================

setlocal

set "EXE_DIR=%~dp0..\src\KpiReport.Etl\KpiReport.Etl\bin\Debug"
set "EXE=%EXE_DIR%\KpiReport.Etl.exe"
set "LOG_DIR=%~dp0logs"
set "LOG=%LOG_DIR%\send-report.log"

if not exist "%EXE%" (
    echo [%date% %time%] ERROR: not found: %EXE%
    exit /b 9
)

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

pushd "%EXE_DIR%"
echo. >> "%LOG%"
echo ===== %date% %time% ===== >> "%LOG%"
"%EXE%" send-report %* >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
echo ----- exit code %RC% >> "%LOG%"
popd

endlocal & exit /b %RC%
