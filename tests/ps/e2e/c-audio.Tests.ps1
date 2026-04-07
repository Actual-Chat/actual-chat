# Detect OS at discovery time (for Describe title and -Skip flags)
$currentOS = if ($IsWindows -or $env:OS -eq "Windows_NT") { "Windows" } elseif ($IsMacOS) { "macOS" } else { "Linux" }

Describe "PulseAudioSetup on $currentOS" {
    BeforeAll {
        . "$PSScriptRoot/../../../scripts/Common.ps1"

        # Source PulseAudioSetup class from c.ps1 by extracting just the class definition
        $scriptContent = Get-Content "$PSScriptRoot/../../../c.ps1" -Raw
        $classPattern = '(?s)(class PulseAudioSetup \{.+?\n\})'
        if ($scriptContent -match $classPattern) {
            Invoke-Expression $Matches[1]
        } else {
            throw "Could not find PulseAudioSetup class in c.ps1"
        }

        $pa = [PulseAudioSetup]::new()
        $os = Get-CurrentOS
    }

    AfterAll {
        if ($IsWindows -or $env:OS -eq "Windows_NT") {
            taskkill /IM pulseaudio.exe /F 2>$null
        } elseif (Get-Command "pulseaudio" -ErrorAction SilentlyContinue) {
            pulseaudio --kill 2>$null
        }
    }

    It "detects OS as '<os>'" {
        $os | Should -BeIn @("Windows", "macOS", "Linux")
    }

    It "is not running before setup" {
        $pa.IsRunning() | Should -BeFalse
    }

    Context "Setup" {
        BeforeAll {
            $pa.Setup()
        }

        It "installs PulseAudio executable" -Skip:($currentOS -ne "Windows") {
            $found = (Test-Path "$env:LOCALAPPDATA\PulseAudio\bin\pulseaudio.exe") -or
                     (Test-Path "$env:ProgramFiles\PulseAudio\bin\pulseaudio.exe")
            $found | Should -BeTrue
        }

        It "installs PulseAudio via Homebrew" -Skip:($currentOS -ne "macOS") {
            Get-Command "pulseaudio" -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
        }

        It "creates config file" {
            $configFile = if ($os -eq "Windows") {
                "$env:APPDATA\PulseAudio\default.pa"
            } else {
                "$env:HOME/.pulse/default.pa"
            }
            $configFile | Should -Exist
        }

        It "config contains TCP module" {
            $configFile = if ($os -eq "Windows") {
                "$env:APPDATA\PulseAudio\default.pa"
            } else {
                "$env:HOME/.pulse/default.pa"
            }
            $configFile | Should -FileContentMatch "module-native-protocol-tcp"
        }

        It "config contains auth-ip-acl" {
            $configFile = if ($os -eq "Windows") {
                "$env:APPDATA\PulseAudio\default.pa"
            } else {
                "$env:HOME/.pulse/default.pa"
            }
            $configFile | Should -FileContentMatch "auth-ip-acl=127\.0\.0\.1"
        }

        It "starts PulseAudio on port 4713" {
            $pa.IsRunning() | Should -BeTrue
        }

        It "is idempotent (second call does not fail)" {
            { $pa.Setup() } | Should -Not -Throw
            $pa.IsRunning() | Should -BeTrue
        }
    }
}
