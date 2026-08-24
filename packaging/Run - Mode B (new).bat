@echo off
REM Mode B - transparency via the Windows desktop compositor (DWM) instead of the older
REM layered-window path. This is the one we're testing.
set CHARMDESK_RENDER=dwm
start "" "%~dp0CharmDesk.exe"
