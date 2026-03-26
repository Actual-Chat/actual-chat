BeforeAll {
    . "$PSScriptRoot/../scripts/Common.ps1"
    $script:projectRoot = Split-Path -Parent $PSScriptRoot
}

Describe "LocalAppServer" {
    BeforeAll {
        # Use isolated temp dir so tests don't interact with a real running server
        $script:isoDir = Join-Path ([System.IO.Path]::GetTempPath()) "test-local-server-$(Get-Random)"
        New-Item -ItemType Directory -Path $script:isoDir -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $script:isoDir "tmp") -Force | Out-Null
        $srcDir = Join-Path $script:isoDir "src" "dotnet" "App.Server"
        New-Item -ItemType Directory -Path $srcDir -Force | Out-Null
        Set-Content (Join-Path $srcDir "App.Server.csproj") "<Project/>"
        Set-Content (Join-Path $script:isoDir ".env") "urls=http://localhost:19877`nCoreSettings__Instance=test-iso`nHostSettings__BaseUri=https://test-iso.local.voxt.ai"
        $script:server = [LocalAppServer]::new($script:isoDir)
    }

    AfterAll {
        Remove-Item $script:isoDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    It "reads instance from .env" {
        $server.Instance | Should -Be "test-iso"
    }

    It "reads port from .env" {
        $server.Port | Should -Be 19877
    }

    It "sets ServerProject to .csproj" {
        $server.ServerProject | Should -BeLike "*.csproj"
    }

    It "sets LogFile to .log" {
        $server.LogFile | Should -BeLike "*.log"
    }

    It "is not running initially" {
        $server.IsRunning() | Should -BeFalse
    }

    Context "GetStatus when stopped" {
        BeforeAll {
            $script:status = $server.GetStatus()
        }

        It "returns status=stopped" {
            $status.status | Should -Be "stopped"
        }

        It "returns correct instance" {
            $status.instance | Should -Be $server.Instance
        }

        It "returns correct baseUri" {
            $status.baseUri | Should -Be $server.BaseUri
        }

        It "returns correct port" {
            $status.port | Should -Be $server.Port
        }

        It "returns null pid" {
            $status.pid | Should -BeNullOrEmpty
        }
    }

    Context "Stop when not running" {
        BeforeAll {
            $script:result = $server.Stop()
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
            $script:log = $server.GetLog(10)
        }

        It "returns log key" {
            $log.ContainsKey("log") | Should -BeTrue
        }

        It "returns stderr key" {
            $log.ContainsKey("stderr") | Should -BeTrue
        }
    }
}

Describe "WatchAgent + RemoteAppServer" {
    BeforeAll {
        # Isolated temp dir so the WatchAgent's LocalAppServer doesn't find a real server
        $script:waIsoDir = Join-Path ([System.IO.Path]::GetTempPath()) "test-watch-agent-$(Get-Random)"
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
        $script:agent = [WatchAgent]::new($script:waIsoDir, $script:agentPort)
        $agent.Start()

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
        $agent.Stop()
        Remove-Item $script:waIsoDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    It "starts successfully" {
        $agent.Process | Should -Not -BeNullOrEmpty
    }

    It "process is running" {
        $agent.Process.HasExited | Should -BeFalse
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
            $client = [RemoteAppServer]::new("http://localhost:$script:agentPort")
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

    Context "Client.Stop when not running" {
        BeforeAll {
            $client = [RemoteAppServer]::new("http://localhost:$script:agentPort")
            $script:result = $client.Stop()
        }

        It "returns stopped=false" {
            $result.stopped | Should -BeFalse
        }
    }

    Context "Client.Start" {
        BeforeAll {
            $client = [RemoteAppServer]::new("http://localhost:$script:agentPort")
            $script:startError = $null
            try {
                $script:startResult = $client.Start($false)
            } catch {
                $script:startError = $_
            }
        }

        AfterAll {
            # Clean up if server was started
            if ($script:startResult -and $script:startResult.started) {
                $client = [RemoteAppServer]::new("http://localhost:$script:agentPort")
                try { $null = $client.Stop() } catch {}
            }
        }

        It "returns a result or a handled error" {
            ($startResult -ne $null -or $startError -ne $null) | Should -BeTrue
        }
    }

    Context "Client.GetLog" {
        BeforeAll {
            $client = [RemoteAppServer]::new("http://localhost:$script:agentPort")
            $script:logResult = $client.GetLog(10)
        }

        It "returns a result" {
            $logResult | Should -Not -BeNullOrEmpty
        }
    }

    Context "Unknown route" {
        It "returns 404" {
            { Invoke-RestMethod -Uri "http://localhost:$script:agentPort/nonexistent" -ErrorAction Stop } |
                Should -Throw "*404*"
        }
    }

    Context "Agent.Stop" {
        It "clears process" {
            # This runs in AfterAll above; verify separately
            $agent2 = [WatchAgent]::new($script:projectRoot, 7899)
            $agent2.Stop()
            $agent2.Process | Should -BeNullOrEmpty
        }
    }
}

Describe "RemoteAppServer.TryCreate" {
    BeforeAll {
        $script:savedPort = $env:AC_WATCH_AGENT_PORT
    }

    AfterAll {
        $env:AC_WATCH_AGENT_PORT = $script:savedPort
    }

    It "returns null when AC_WATCH_AGENT_PORT is not set" {
        $env:AC_WATCH_AGENT_PORT = ""
        [RemoteAppServer]::TryCreate() | Should -BeNullOrEmpty
    }

    It "returns null when agent is not reachable" {
        $env:AC_WATCH_AGENT_PORT = "19999"
        [RemoteAppServer]::TryCreate() | Should -BeNullOrEmpty
    }
}

Describe "LocalAppServer.TryReconnect" {
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
        $server = [LocalAppServer]::new($script:tempDir)
        $server.PidFile | Should -BeLike "*/tmp/server-dev.pid"
    }

    Context "with valid PID file" {
        BeforeAll {
            $psi = [System.Diagnostics.ProcessStartInfo]::new("sleep", "300")
            $psi.UseShellExecute = $false
            $script:proc = [System.Diagnostics.Process]::Start($psi)
            Set-Content (Join-Path $script:tempDir "tmp" "server-dev.pid") $script:proc.Id
            $script:server = [LocalAppServer]::new($script:tempDir)
        }

        AfterAll {
            try { $script:proc.Kill() } catch {}
            Remove-Item (Join-Path $script:tempDir "tmp" "server-dev.pid") -ErrorAction SilentlyContinue
        }

        It "reconnects to the process" {
            $script:server.IsRunning() | Should -BeTrue
            $script:server.Process.Id | Should -Be $script:proc.Id
        }

        It "reports running in GetStatus" {
            $status = $script:server.GetStatus()
            $status.status | Should -Be "running"
            $status.pid | Should -Be $script:proc.Id
        }
    }

    Context "with stale PID file" {
        BeforeAll {
            $script:pidFile = Join-Path $script:tempDir "tmp" "server-dev.pid"
            Set-Content $script:pidFile "999999999"
            $script:server = [LocalAppServer]::new($script:tempDir)
        }

        It "does not reconnect" {
            $script:server.IsRunning() | Should -BeFalse
        }

        It "removes the stale PID file" {
            Test-Path $script:pidFile | Should -BeFalse
        }
    }

    Context "Stop removes PID file" {
        BeforeAll {
            $psi = [System.Diagnostics.ProcessStartInfo]::new("sleep", "300")
            $psi.UseShellExecute = $false
            $script:proc = [System.Diagnostics.Process]::Start($psi)
            $script:pidFile = Join-Path $script:tempDir "tmp" "server-dev.pid"
            Set-Content $script:pidFile $script:proc.Id
            $script:server = [LocalAppServer]::new($script:tempDir)
        }

        It "PID file exists before stop" {
            Test-Path $script:pidFile | Should -BeTrue
        }

        It "removes PID file after stop" {
            $script:server.Stop()
            Test-Path $script:pidFile | Should -BeFalse
        }

        It "reports stopped after stop" {
            $script:server.GetStatus().status | Should -Be "stopped"
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
            $script:server = [LocalAppServer]::new($script:tempDir)
        }

        AfterAll {
            try { $script:listener.Kill() } catch {}
            Set-Content (Join-Path $script:tempDir ".env") "urls=http://localhost:19876"
            Remove-Item (Join-Path $script:tempDir "tmp" "server-dev.pid") -ErrorAction SilentlyContinue
        }

        It "detects process via lsof" {
            $script:server.IsRunning() | Should -BeTrue
            $script:server.Process.Id | Should -Be $script:listener.Id
        }

        It "creates PID file from port discovery" {
            $pidFile = Join-Path $script:tempDir "tmp" "server-dev.pid"
            Test-Path $pidFile | Should -BeTrue
            [int](Get-Content $pidFile -Raw).Trim() | Should -Be $script:listener.Id
        }
    }
}

Describe "AppServerFactory.Create" {
    BeforeAll {
        $script:savedPort = $env:AC_WATCH_AGENT_PORT
    }

    AfterAll {
        $env:AC_WATCH_AGENT_PORT = $script:savedPort
    }

    It "returns LocalAppServer when no watch agent" {
        $env:AC_WATCH_AGENT_PORT = ""
        $server = [AppServerFactory]::Create($script:projectRoot)
        $server.GetType().Name | Should -Be "LocalAppServer"
    }

    It "returns LocalAppServer when agent unreachable" {
        $env:AC_WATCH_AGENT_PORT = "19999"
        $server = [AppServerFactory]::Create($script:projectRoot)
        $server.GetType().Name | Should -Be "LocalAppServer"
    }
}
