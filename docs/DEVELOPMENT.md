# Working on the improved fork

## Local layout

Keep your VS Code workspace beside the Git checkout.  For example, create a container folder named
`TldSaveEditorWindows_FORK`, clone the repository into its `repo` subfolder, and put
`TldSaveEditorWindows_FORK.code-workspace` in the container folder.

| Path relative to the container | Purpose |
|---|---|
| `repo/` | Git checkout, including its `.git` directory |
| `TldSaveEditorWindows_FORK.code-workspace` | Your local VS Code workspace, outside the checkout |

The workspace file contains:

```json
{
  "folders": [
    {
      "name": "TLD Save Editor",
      "path": "repo"
    }
  ]
}
```

Open that workspace with **File > Open Workspace from File**.  It needs no repository ignore rule because
it is outside the Git checkout.  Put personal workspace settings, tasks, and launch configurations in this
file when you want to keep them local.

## Git remotes

From the container folder:

```powershell
git clone https://github.com/dismalnow/TldSaveEditorWindows_Improveed.git repo
Set-Location repo
git remote add upstream https://github.com/YohanWD/TldSaveEditorWindows.git
git remote -v
```

`origin` is this fork, where development branches are pushed.  `upstream` is YohanWD's original project.
Fetching from upstream downloads history for comparison; it does not merge changes into your files.

To work on the first improvement branch after cloning:

```powershell
git fetch origin
git switch --track origin/improvements/save-integrity-and-diagnostics
```

For subsequent updates on that branch, with your local changes committed or deliberately stashed:

```powershell
git pull --ff-only
```

For your own change, create a branch from the version you intend to extend, review the diff, commit the
selected files, and publish that branch:

```powershell
git switch -c feature/describe-the-change
git diff
git status
# Stage the specific files you intend to commit, then:
git commit -m "Describe the behavior changed"
git push --set-upstream origin HEAD
```

The VS Code Source Control view uses the same Git checkout as these commands.  Commit records a local
snapshot.  Push uploads commits to the selected remote branch.  Pull brings remote commits into the
current branch.  VS Code's Sync action pulls and then pushes.

## Build and run

Development requires the **.NET 10 SDK**.  Running the WPF application requires Windows.  The C# extension
for VS Code provides language support; the terminal commands below are sufficient to build and test.

Run these from `repo`:

```powershell
dotnet --list-sdks
dotnet build TldSaveEditor.slnx -c Release
dotnet test tests/TldSave.Core.Tests -c Release
dotnet run --project src/TldSaveEditor
```

To produce a self-contained Windows executable:

```powershell
dotnet publish src/TldSaveEditor -p:PublishProfile=win-x64
```

The executable is written to `publish/win-x64/TldSaveEditor.exe`.

Core tests can also run on Linux.  To compile the Windows application from a non-Windows build host,
pass `-p:EnableWindowsTargeting=true`; that does not make the WPF application runnable on that host.

## Pull requests and previews

The Build workflow runs on branch pushes and pull requests.  A successful run attaches a Windows preview
ZIP, which includes the executable and its source commit/build provenance.  Artifacts expire after 14 days.
Tests must pass before packaging.  A preview's successful automated checks do not establish compatibility
with a particular installed game build.

Keep game saves used during personal testing outside Git.  The two existing public test fixtures remain
the automated baseline.  Record exact game build, mode, edits, and the load/save/reload result before
making an in-game compatibility claim.  See [COMPATIBILITY.md](COMPATIBILITY.md).

## Sources

- [VS Code workspaces](https://code.visualstudio.com/docs/editing/workspaces/workspaces)
- [VS Code source control](https://code.visualstudio.com/docs/sourcecontrol/overview)
- [.NET SDK downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

