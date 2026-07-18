@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "WEB_DIR=%SCRIPT_DIR%..\web"
set "NODE_DIR=%SCRIPT_DIR%..\node"

if "%NODE_ENV%"=="" set "NODE_ENV=production"
if "%HOSTNAME%"=="" set "HOSTNAME=127.0.0.1"
if "%PORT%"=="" set "PORT=3000"

if not "%MEMORIX_NODE_BIN%"=="" (
  set "NODE_BIN=%MEMORIX_NODE_BIN%"
) else if exist "%NODE_DIR%\bin\node.exe" (
  set "NODE_BIN=%NODE_DIR%\bin\node.exe"
) else if exist "%NODE_DIR%\node.exe" (
  set "NODE_BIN=%NODE_DIR%\node.exe"
) else if exist "%NODE_DIR%\node" (
  set "NODE_BIN=%NODE_DIR%\node"
) else (
  set "NODE_BIN=node"
)

cd /d "%WEB_DIR%"
"%NODE_BIN%" server.js
