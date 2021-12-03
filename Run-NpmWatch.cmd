@echo off
pushd src\nodejs
call npm install
start cmd /C npm run watch
popd
