set PATH=%PATH%;"C:\Program Files (x86)\Android\android-sdk\platform-tools"
dotnet build -f:net7.0-android -c:Debug -t:FinishAotProfiling -p:RunAOTCompilation=false
