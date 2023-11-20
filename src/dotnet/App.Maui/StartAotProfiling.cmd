set PATH=%PATH%;"C:\Program Files (x86)\Android\android-sdk\platform-tools"
dotnet build -f:net8.0-android /p:IsProfilingEnabled=true -c:Debug -t:BuildAndStartAotProfiling
