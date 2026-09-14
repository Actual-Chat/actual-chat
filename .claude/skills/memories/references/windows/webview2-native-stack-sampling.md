The Windows MSIX Voxt app launches WebView2 without `--remote-debugging-port`, so
CDP is unavailable on a session that is already hung. Native stack sampling works
instead and needs no debugger attach:

1. **Find the hot thread.** `Get-Process -Id <renderer>` then diff
   `$p.Threads[].TotalProcessorTime` over ~5 s. The renderer's *first* thread is
   main/JS. A full core there = a wedged page; `Responding` on the host window
   stays `True`, because the WinUI pump is fine and only the WebView is stuck.
2. **Dump it.** P/Invoke `dbghelp!MiniDumpWriteDump` with
   `OpenProcess(QUERY_INFORMATION|VM_READ)`. Type `0x1000` gives a ~0.3 MB
   stacks-only dump in <1 s, so you can take 16 of them 1.2 s apart and get a
   real profile. Type `0x1826` gives the full ~3 GB one. **Never `procdump`** —
   see [[windows-app-audio-diagnosis]].
3. **Symbolize.** `winget install Microsoft.WinDbg` puts `cdb.exe` in
   `C:\Program Files\WindowsApps\Microsoft.WinDbg_*_x64__8wekyb3d8bbwe\amd64\`.
   Edge/WebView2 PDBs are on `https://msdl.microsoft.com/download/symbols`
   (~600 MB first fetch). `cdb -z <dump> -y srv*<cache>*<url> -c "~~[0x<tid hex>]s; kn 120; q"`.
4. **One stack proves nothing** — the first dump landed in `:has()` style recalc,
   which turned out to be 1 observation in 20. Histogram the samples.
5. `dps` over the stack frames recovers argument pointers even without private
   symbols: a `std::__introsort` range gave the sorted array's exact element count,
   and a changing array address per sample proved it was many calls, not one slow one.

For the managed side, `dotnet-dump` needs the app's own DAC: copy
`mscordaccore.dll`/`coreclr.dll` out of the `WindowsApps` package to a
space-free path and `setclrpath` to it.
