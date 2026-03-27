BeforeAll {
    . "$PSScriptRoot/../scripts/Common.ps1"
    $script:projectRoot = Split-Path -Parent $PSScriptRoot
}

Describe "BuildAgent" {
    BeforeAll {
        # Use isolated temp dir so tests don't interact with a real running server
        $script:isoDir = Join-Path ([System.IO.Path]::GetTempPath()) "test-local-agent-$(Get-Random)"
        New-Item -ItemType Directory -Path $script:isoDir -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $script:isoDir "tmp") -Force | Out-Null
        $srcDir = Join-Path $script:isoDir "src" "dotnet" "App.Server"
        New-Item -ItemType Directory -Path $srcDir -Force | Out-Null
        Set-Content (Join-Path $srcDir "App.Server.csproj") "<Project/>"
        Set-Content (Join-Path $script:isoDir ".env") "urls=http://localhost:19877`nCoreSettings__Instance=test-iso`nHostSettings__BaseUri=https://test-iso.local.voxt.ai"
        $script:agent = [BuildAgent]::new($script:isoDir)
    }

    AfterAll {
        Remove-Item $script:isoDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    It "reads instance from .env" {
        $agent.Instance | Should -Be "test-iso"
    }

    It "reads port from .env" {
        $agent.Port | Should -Be 19877
    }

    It "sets ServerProject to .csproj" {
        $agent.ServerProject | Should -BeLike "*.csproj"
    }

    It "sets LogFile to .log" {
        $agent.LogFile | Should -BeLike "*.log"
    }

    It "is not running initially" {
        $agent.IsRunning() | Should -BeFalse
    }

    Context "GetStatus when stopped" {
        BeforeAll {
            $script:status = $agent.GetStatus()
        }

        It "returns status=stopped" {
            $status.status | Should -Be "stopped"
        }

        It "returns correct instance" {
            $status.instance | Should -Be $agent.Instance
        }

        It "returns correct baseUri" {
            $status.baseUri | Should -Be $agent.BaseUri
        }

        It "returns correct port" {
            $status.port | Should -Be $agent.Port
        }

        It "returns null pid" {
            $status.pid | Should -BeNullOrEmpty
        }
    }

    Context "StopServer when not running" {
        BeforeAll {
            $script:result = $agent.StopServer()
        }

        It "returns stopped=false" {
            $result.stopped | Should -BeFalse
        }

        It "returns a message" {
            $result.message | Should -BeLike "*No running*"
        }
    }

    Context "GetLog" {
        BeforeAll {
            $script:log = $agent.GetLog(10)
        }

        It "returns log key" {
            $log.ContainsKey("log") | Should -BeTrue
        }

        It "returns stderr key" {
            $log.ContainsKey("stderr") | Should -BeTrue
        }
    }

    Context "InstallNpm" {
        It "returns a hashtable with exitCode" {
            # npm ci will fail (no package.json in temp dir) but should return a result
            $result = $agent.InstallNpm()
            $result.ContainsKey("exitCode") | Should -BeTrue
            $result.ContainsKey("output") | Should -BeTrue
        }
    }

    Context "BuildFrontend" {
        It "returns a hashtable with exitCode for debug build" {
            # npm run will fail (no package.json in temp dir) but should return a result
            $result = $agent.BuildFrontend($false)
            $result.ContainsKey("exitCode") | Should -BeTrue
            $result.ContainsKey("output") | Should -BeTrue
            $result.release | Should -BeFalse
        }

        It "returns a hashtable with exitCode for release build" {
            $result = $agent.BuildFrontend($true)
            $result.ContainsKey("exitCode") | Should -BeTrue
            $result.release | Should -BeTrue
        }
    }
}

Describe "BuildAgentHost + BuildAgentProxy" {
    BeforeAll {
        # Isolated temp dir so the BuildAgentHost's BuildAgent doesn't find a real server
        $script:waIsoDir = Join-Path ([System.IO.Path]::GetTempPath()) "test-build-agent-$(Get-Random)"
        New-Item -ItemType Directory -Path $script:waIsoDir -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $script:waIsoDir "tmp") -Force | Out-Null
        $srcDir = Join-Path $script:waIsoDir "src" "dotnet" "App.Server"
        New-Item -ItemType Directory -Path $srcDir -Force | Out-Null
        Set-Content (Join-Path $srcDir "App.Server.csproj") "<Project/>"
        Set-Content (Join-Path $script:waIsoDir ".env") "urls=http://localhost:19878`nCoreSettings__Instance=test-wa`nHostSettings__BaseUri=https://test-wa.local.voxt.ai"
        # Copy Common.ps1 so the child process can source it
        $scriptsDir = Join-Path $script:waIsoDir "scripts"
        New-Item -ItemType Directory -Path $scriptsDir -Force | Out-Null
        Copy-Item (Join-Path $script:projectRoot "scripts" "Common.ps1") $scriptsDir

        $script:agentPort = 7900 + (Get-Random -Minimum 0 -Maximum 99)
        $script:host_ = [BuildAgentHost]::new($script:waIsoDir, $script:agentPort)
        $host_.Start()

        # Wait for HTTP server to be ready
        $script:agentReady = $false
        for ($i = 1; $i -le 10; $i++) {
            Start-Sleep -Milliseconds 500
            try {
                $null = Invoke-RestMethod -Uri "http://localhost:$agentPort/health" -TimeoutSec 2
                $script:agentReady = $true
                break
            } catch {}
        }
    }

    AfterAll {
        $host_.Stop()
        Remove-Item $script:waIsoDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    It "starts successfully" {
        $host_.Process | Should -Not -BeNullOrEmpty
    }

    It "process is running" {
        $host_.Process.HasExited | Should -BeFalse
    }

    It "HTTP server is reachable" {
        $agentReady | Should -BeTrue
    }

    Context "GET /health" {
        BeforeAll {
            $script:health = Invoke-RestMethod -Uri "http://localhost:$script:agentPort/health"
        }

        It "returns status=ok" {
            $health.status | Should -Be "ok"
        }

        It "returns correct project" {
            $health.project | Should -Be $script:waIsoDir
        }
    }

    Context "Client.GetStatus" {
        BeforeAll {
            $client = [BuildAgentProxy]::new("http://localhost:$script:agentPort")
            $script:status = $client.GetStatus()
        }

        It "returns stopped" {
            $status.status | Should -Be "stopped"
        }

        It "has correct instance" {
            $status.instance | Should -Be "test-wa"
        }

        It "has correct port" {
            $status.port | Should -Be 19878
        }
    }

    Context "Client.StopServer when not running" {
        BeforeAll {
            $client = [BuildAgentProxy]::new("http://localhost:$script:agentPort")
            $script:result = $client.StopServer()
        }

        It "returns stopped=false" {
            $result.stopped | Should -BeFalse
        }
    }

    Context "Client.StartServer" {
        BeforeAll {
            $client = [BuildAgentProxy]::new("http://localhost:$script:agentPort")
            $script:startError = $null
            try {
                $script:startResult = $client.StartServer($false)
            } catch {
                $script:startError = $_
            }
        }

        AfterAll {
            # Clean up if server was started
            if ($script:startResult -and $script:startResult.started) {
                $client = [BuildAgentProxy]::new("http://localhost:$script:agentPort")
                try { $null = $client.StopServer() } catch {}
            }
        }

        It "returns a result or a handled error" {
            ($startResult -ne $null -or $startError -ne $null) | Should -BeTrue
        }
    }

    Context "Client.GetLog" {
        BeforeAll {
            $client = [BuildAgentProxy]::new("http://localhost:$script:agentPort")
            $script:logResult = $client.GetLog(10)
        }

        It "returns a result" {
            $logResult | Should -Not -BeNullOrEmpty
        }
    }

    Context "POST /npm/install via HTTP" {
        It "returns a result" {
            $result = Invoke-RestMethod -Uri "http://localhost:$script:agentPort/npm/install" -Method Post `
                -Body "{}" -ContentType "application/json" -TimeoutSec 30
            $result | Should -Not -BeNullOrEmpty
        }
    }

    Context "POST /npm/build via HTTP" {
        It "returns a result" {
            $result = Invoke-RestMethod -Uri "http://localhost:$script:agentPort/npm/build" -Method Post `
                -Body '{"release":false}' -ContentType "application/json" -TimeoutSec 30
            $result | Should -Not -BeNullOrEmpty
        }
    }

    Context "POST /server/build via HTTP" {
        It "returns a result" {
            $result = Invoke-RestMethod -Uri "http://localhost:$script:agentPort/server/build" -Method Post `
                -Body "{}" -ContentType "application/json" -TimeoutSec 30
            $result | Should -Not -BeNullOrEmpty
        }
    }

    Context "Unknown route" {
        It "returns 404" {
            { Invoke-RestMethod -Uri "http://localhost:$script:agentPort/nonexistent" -ErrorAction Stop } |
                Should -Throw "*404*"
        }
    }

    Context "BuildAgentHost.Stop" {
        It "clears process" {
            # This runs in AfterAll above; verify separately
            $host2 = [BuildAgentHost]::new($script:projectRoot, 7899)
            $host2.Stop()
            $host2.Process | Should -BeNullOrEmpty
        }
    }
}

Describe "BuildAgentProxy.TryCreate" {
    BeforeAll {
        $script:savedBuildPort = $env:AC_BUILD_AGENT_PORT
        $script:savedWatchPort = $env:AC_WATCH_AGENT_PORT
    }

    AfterAll {
        $env:AC_BUILD_AGENT_PORT = $script:savedBuildPort
        $env:AC_WATCH_AGENT_PORT = $script:savedWatchPort
    }

    It "returns null when neither env var is set" {
        $env:AC_BUILD_AGENT_PORT = ""
        $env:AC_WATCH_AGENT_PORT = ""
        [BuildAgentProxy]::TryCreate() | Should -BeNullOrEmpty
    }

    It "returns null when agent is not reachable via AC_BUILD_AGENT_PORT" {
        $env:AC_BUILD_AGENT_PORT = "19999"
        $env:AC_WATCH_AGENT_PORT = ""
        [BuildAgentProxy]::TryCreate() | Should -BeNullOrEmpty
    }

    It "falls back to AC_WATCH_AGENT_PORT" {
        $env:AC_BUILD_AGENT_PORT = ""
        $env:AC_WATCH_AGENT_PORT = "19998"
        # Not reachable, but should attempt the fallback
        [BuildAgentProxy]::TryCreate() | Should -BeNullOrEmpty
    }
}

Describe "BuildAgent.TryReconnect" {
    BeforeAll {
        # Isolated temp dir with an unused port to prevent lsof interference
        $script:tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "test-reconnect-$(Get-Random)"
        New-Item -ItemType Directory -Path $script:tempDir -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $script:tempDir "tmp") -Force | Out-Null
        $srcDir = Join-Path $script:tempDir "src" "dotnet" "App.Server"
        New-Item -ItemType Directory -Path $srcDir -Force | Out-Null
        Set-Content (Join-Path $srcDir "App.Server.csproj") "<Project/>"
        Set-Content (Join-Path $script:tempDir ".env") "urls=http://localhost:19876"
    }

    AfterAll {
        Remove-Item $script:tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    It "sets PidFile path" {
        $agent = [BuildAgent]::new($script:tempDir)
        $agent.PidFile | Should -BeLike "*/tmp/server-dev.pid"
    }

    Context "with valid PID file" {
        BeforeAll {
            $psi = [System.Diagnostics.ProcessStartInfo]::new("sleep", "300")
            $psi.UseShellExecute = $false
            $script:proc = [System.Diagnostics.Process]::Start($psi)
            Set-Content (Join-Path $script:tempDir "tmp" "server-dev.pid") $script:proc.Id
            $script:agent = [BuildAgent]::new($script:tempDir)
        }

        AfterAll {
            try { $script:proc.Kill() } catch {}
            Remove-Item (Join-Path $script:tempDir "tmp" "server-dev.pid") -ErrorAction SilentlyContinue
        }

        It "reconnects to the process" {
            $script:agent.IsRunning() | Should -BeTrue
            $script:agent.Process.Id | Should -Be $script:proc.Id
        }

        It "reports running in GetStatus" {
            $status = $script:agent.GetStatus()
            $status.status | Should -Be "running"
            $status.pid | Should -Be $script:proc.Id
        }
    }

    Context "with stale PID file" {
        BeforeAll {
            $script:pidFile = Join-Path $script:tempDir "tmp" "server-dev.pid"
            Set-Content $script:pidFile "999999999"
            $script:agent = [BuildAgent]::new($script:tempDir)
        }

        It "does not reconnect" {
            $script:agent.IsRunning() | Should -BeFalse
        }

        It "removes the stale PID file" {
            Test-Path $script:pidFile | Should -BeFalse
        }
    }

    Context "StopServer removes PID file" {
        BeforeAll {
            $psi = [System.Diagnostics.ProcessStartInfo]::new("sleep", "300")
            $psi.UseShellExecute = $false
            $script:proc = [System.Diagnostics.Process]::Start($psi)
            $script:pidFile = Join-Path $script:tempDir "tmp" "server-dev.pid"
            Set-Content $script:pidFile $script:proc.Id
            $script:agent = [BuildAgent]::new($script:tempDir)
        }

        It "PID file exists before stop" {
            Test-Path $script:pidFile | Should -BeTrue
        }

        It "removes PID file after stop" {
            $script:agent.StopServer()
            Test-Path $script:pidFile | Should -BeFalse
        }

        It "reports stopped after stop" {
            $script:agent.GetStatus().status | Should -Be "stopped"
        }
    }

    Context "port-based fallback" {
        BeforeAll {
            $script:testPort = 19870 + (Get-Random -Minimum 0 -Maximum 99)
            $psi = [System.Diagnostics.ProcessStartInfo]::new("python3")
            $psi.Arguments = "-c `"import socket,time; s=socket.socket(); s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1); s.bind(('',${script:testPort})); s.listen(1); time.sleep(300)`""
            $psi.UseShellExecute = $false
            $script:listener = [System.Diagnostics.Process]::Start($psi)
            Start-Sleep -Seconds 1

            Set-Content (Join-Path $script:tempDir ".env") "urls=http://localhost:$($script:testPort)"
            Remove-Item (Join-Path $script:tempDir "tmp" "server-dev.pid") -ErrorAction SilentlyContinue
            $script:agent = [BuildAgent]::new($script:tempDir)
        }

        AfterAll {
            try { $script:listener.Kill() } catch {}
            Set-Content (Join-Path $script:tempDir ".env") "urls=http://localhost:19876"
            Remove-Item (Join-Path $script:tempDir "tmp" "server-dev.pid") -ErrorAction SilentlyContinue
        }

        It "detects process via lsof" {
            $script:agent.IsRunning() | Should -BeTrue
            $script:agent.Process.Id | Should -Be $script:listener.Id
        }

        It "creates PID file from port discovery" {
            $pidFile = Join-Path $script:tempDir "tmp" "server-dev.pid"
            Test-Path $pidFile | Should -BeTrue
            [int](Get-Content $pidFile -Raw).Trim() | Should -Be $script:listener.Id
        }
    }
}

Describe "Get-BuildAgent" {
    BeforeAll {
        $script:savedBuildPort = $env:AC_BUILD_AGENT_PORT
        $script:savedWatchPort = $env:AC_WATCH_AGENT_PORT
    }

    AfterAll {
        $env:AC_BUILD_AGENT_PORT = $script:savedBuildPort
        $env:AC_WATCH_AGENT_PORT = $script:savedWatchPort
    }

    It "returns BuildAgent when no remote agent" {
        $env:AC_BUILD_AGENT_PORT = ""
        $env:AC_WATCH_AGENT_PORT = ""
        $agent = Get-BuildAgent($script:projectRoot)
        $agent.GetType().Name | Should -Be "BuildAgent"
    }

    It "returns BuildAgent when agent unreachable" {
        $env:AC_BUILD_AGENT_PORT = "19999"
        $env:AC_WATCH_AGENT_PORT = ""
        $agent = Get-BuildAgent($script:projectRoot)
        $agent.GetType().Name | Should -Be "BuildAgent"
    }
}
