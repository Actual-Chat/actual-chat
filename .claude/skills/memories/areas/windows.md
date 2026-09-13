# Windows app quirks

Each entry is a couple of sentences. The full write-up — commands, evidence, dates — is in
`../references/windows/<name>.md`; open it when the one-liner turns out to matter.

**Don't launch the Windows dev app to test a shared-UI change.** It opens a "Voxt (Dev)" window
on Alex's desktop and steals his screen; iterate with `/server-loop` instead and ask before
launching for a genuinely MAUI-only change.

- **The Windows app has no usable logs.** Its log file has been frozen since 2024 and Sentry is
  dead for it, so diagnose "recording won't start" from external signals — chiefly the mic
  `ConsentStore` registry keys. Never run procdump under a timeout.
  → `../references/windows/windows-app-audio-diagnosis.md`
- **A hung or spinning WebView2 renderer has no remote-debugging port**, so CDP is not an option:
  take repeated `MiniDumpWriteDump` snapshots, resolve them with `cdb` against the Microsoft
  symbol server, and histogram the samples to see where it actually sits.
  → `../references/windows/webview2-native-stack-sampling.md`
- **Windows echo cancellation is the native WebRTC APM plus WASAPI loopback**, not a browser
  feature. To investigate it, replicate outside the repo, A/B both paths in one process, and keep
  levels above −36 dBFS — below that the traps make a working AEC look broken.
  → `../references/windows/windows-aec-investigation.md`

Host-machine facts (127.0.0.1 vs localhost, `python` vs `python3`) live in global user memory,
not here.
