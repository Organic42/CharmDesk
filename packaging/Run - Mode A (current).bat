@echo off
REM Mode A - the current rendering path (WPF AllowsTransparency / layered window).
REM This is what CharmDesk.exe does if you just double-click it normally.
set CHARMDESK_RENDER=layered
start "" "%~dp0CharmDesk.exe"
