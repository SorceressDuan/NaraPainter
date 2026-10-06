@echo off
rem Launches the app from this folder. Exists because the folder holds several hundred runtime files
rem and the user should not have to find the executable among them.
start "" "%~dp0NaraDreamPainter.exe"
