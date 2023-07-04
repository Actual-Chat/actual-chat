:;# This is to tell Git that you want your own independent version of the file or folder
:;# We use approach described at https://stackoverflow.com/questions/936249/how-to-stop-tracking-and-ignore-changes-to-a-file-in-git/40272289#40272289
:;# To list all skipped files, use command from https://stackoverflow.com/questions/42363881/how-to-list-files-ignored-with-skip-worktree

:;# google-services.json config files are stored separately. If we change them locally, we don't want they are tracked in the repo.
git update-index --skip-worktree ./src/dotnet/App.Maui/Platforms/Android/Resources/google-services.json.dev
git update-index --skip-worktree ./src/dotnet/App.Maui/Platforms/Android/Resources/google-services.json.prod
git update-index --skip-worktree ./src/dotnet/App.Maui/Platforms/iOS/GoogleService-Info.plist.dev
git update-index --skip-worktree ./src/dotnet/App.Maui/Platforms/iOS/GoogleService-Info.plist.prod
