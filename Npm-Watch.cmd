@echo off
pushd src\nodejs
call npm install
call npm run watch
popd
