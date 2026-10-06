@echo off
rem Creates a shortcut next to this file. A shortcut stores an absolute target path, so one shipped
rem ready-made would point at whatever folder it was built in; building it here uses the final path.
rem
rem This file is deliberately plain ASCII. cmd.exe reads a batch file in the OEM code page, so Chinese
rem written here would be mojibake by the time it is echoed. The wording the user sees comes from the
rem PowerShell command below.
setlocal
set "HERE=%~dp0"
set "LAUNCHER=%HERE%launcher\NaraDreamPainter.exe"

if not exist "%LAUNCHER%" (
    echo Could not find launcher\NaraDreamPainter.exe next to this file.
    echo Unzip the whole archive before running this.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "try {" ^
  "  $s = (New-Object -ComObject WScript.Shell).CreateShortcut('%HERE%NaraDreamPainter.lnk');" ^
  "  $s.TargetPath = '%LAUNCHER%';" ^
  "  $s.WorkingDirectory = '%HERE%launcher';" ^
  "  $s.IconLocation = '%LAUNCHER%,0';" ^
  "  $s.Description = 'Nara Dream Painter';" ^
  "  $s.Save();" ^
  "} catch {" ^
  "  Write-Host ('Could not create the shortcut: ' + $_.Exception.Message);" ^
  "  exit 1" ^
  "}"

if exist "%HERE%NaraDreamPainter.lnk" (
    echo Shortcut created: NaraDreamPainter.lnk - drag it to the desktop or the Start menu.
) else (
    echo Could not create the shortcut.
)
pause
