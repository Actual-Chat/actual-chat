set PATH=%PATH%;"C:\Program Files (x86)\Android\android-sdk\platform-tools"
dotnet build -f:net7.0-android -c:Release -t:BuildAndStartAotProfiling
rem -p:RunAOTCompilation=false
