@echo off
rem Starts the application. The shortcut next to this file does the same thing; this exists for
rem people who prefer a batch file.
rem
rem The launcher already runs as a windowed program, so cmd has nothing to wait for, but the console
rem this file itself opens would still flash. Starting detached and closing immediately is what keeps
rem that from happening.
start "" "%~dp0launcher\NaraDreamPainter.exe" %*
exit /b 0
