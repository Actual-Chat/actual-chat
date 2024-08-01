set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://localhost:7086

start cmd /C dotnet run --no-launch-profile --project src/dotnet/App.Server/App.Server.csproj
