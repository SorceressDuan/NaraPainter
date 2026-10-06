@echo off
rem Starts the application. The shortcut next to this file does the same thing; this exists for
rem people who prefer a batch file, and it runs the launcher rather than the application so that the
rem working directory is set the way the application expects.
start "" "%~dp0launcher\NaraDreamPainter.exe" %*
