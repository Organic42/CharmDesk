@echo off
REM Forces the OLD layered-window rendering path.
REM Only needed if the normal CharmDesk.exe misbehaves on your machine - the default path
REM is the one that fixed the flickering, so try plain CharmDesk.exe first.
set CHARMDESK_RENDER=layered
start "" "%~dp0CharmDesk.exe"
