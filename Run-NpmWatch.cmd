@echo off
pushd src\nodejs
rem call npm install
start cmd /C npm run watch
popd
