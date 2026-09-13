On Windows, `git rebase`/`git checkout` can fail with
`error: unable to unlink old '<path>': Invalid argument` while the same file
renames fine. That combination means some still-running process holds the file
**memory-mapped** — Windows allows renaming a mapped file but not deleting it.
Hit on 2026-09-10 rebasing `src/dotnet/App.Maui/_Profiling/android.mibc` after
`dotnet-pgo` runs; the mapping outlived the tool, held by an idle .NET host.

**Why:** it is not a lock held by an editor or a build, so retrying, `git gc`,
or waiting never clears it, and killing the idle `dotnet`/`MSBuild` nodes may
belong to other work (e.g. `/server-loop`).

**How to apply:** move the mapped file out of the path, let git recreate it,
then redo the operation:

```bash
mv <path> tmp/lockaside/ && git checkout -- <path> && git rebase origin/dev
```

The recreated file is unmapped, so the unlink succeeds. Applies to any large
binary the toolchain mmaps — `.mibc` profiles especially, see [[mibc-update-windows-replaces-profile]].
