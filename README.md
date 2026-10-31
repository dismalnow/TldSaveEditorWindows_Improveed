# TLD Survival Save Editor Improved

This is [dismalnow's fork](https://github.com/dismalnow/TldSaveEditorWindows_Improveed) of
[YohanWD/TldSaveEditorWindows](https://github.com/YohanWD/TldSaveEditorWindows), based on upstream v1.2.1.
The first improvement build is **1.3.0-preview.1**.  It focuses on preserving saves and making editing more predictable.

## What this fork improves

- Unedited sections retain their original compressed data even when their JSON has different whitespace or number formatting.
- Saving refuses to overwrite a changed or deleted source file.  The check includes file contents, and staged writes check the destination again before replacement.
- Each write uses its own temporary file.  Invalid save structure, duplicate JSON keys, and trailing JSON content are rejected before replacement.
- Numeric edits reject NaN and infinity.  Maxing skills preserves missing or unsupported skill data, and the UI blocks saves with invalid numeric input.
- Refresh keeps pending edits, and newly added items are revealed when an inventory filter would hide them.
- Branches and pull requests build and test automatically, then provide an unsigned Windows preview ZIP.

See [compatibility and verification](docs/COMPATIBILITY.md) for the difference between automated checks and
in-game testing.  See [development setup](docs/DEVELOPMENT.md) for Git remotes, VS Code workspaces outside the
repository, and build commands.

## Original editor features

A save editor for **The Long Dark, Survival mode**, for Windows.

- **Player:** health, calories, thirst, fatigue, cold, Well Fed, skill levels, and curing afflictions
- **Inventory:** change quantities and condition, remove items, and add new items from a list of 440+ game items
- **Overview:** experience mode, region, days survived
- Makes a backup before every save, so you can always roll back
- **Backup now:** a one-click snapshot of a save before something risky in game, without editing it
- **Restore backup…:** put any backup back, with a warning before anything is overwritten

Based on [TLD-Save-Editor-3](https://github.com/FINDarkside/TLD-Save-Editor-3) by FINDarkside. This is a C# / WPF
rewrite of its save format logic: LZF compression, nested JSON sections, the inventory structure and the
original item list.

> ⚠️ **Always quit The Long Dark completely before editing a save.** See [why](#why-the-game-must-be-closed).

*The Long Dark is a trademark of Hinterland Studio Inc. This is an unofficial fan-made tool, not affiliated with
or endorsed by Hinterland Studio. Use at your own risk and keep backups of your saves.*

Licensed under the [MIT License](LICENSE). Third-party notices: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

---

## Download

For development previews, open this fork's **Actions** tab, choose a successful **Build** run for the branch
or pull request, and download its `TldSaveEditor-...-win-x64-unsigned` artifact.  Extract the artifact and the
ZIP inside it, then run **`TldSaveEditor.exe`**.  GitHub requires sign-in to download Actions artifacts.

Published versions, when available, appear on this fork's **Releases** page as `TldSaveEditor-x.y.z-win-x64.zip`.
The original upstream releases are separate from this fork's preview builds.

Nothing else to install: the .NET runtime is included in the exe. It needs **Windows 10 or 11, 64-bit**.

The executable is unsigned.  Preview packages include `PREVIEW.txt`, identifying the source commit and build run.

---

## How to use

### 1. Quit the game
Exit The Long Dark **completely**, back to the desktop. Going back to the main menu is not enough.

### 2. Open your save
Start `TldSaveEditor.exe`. The left panel lists your Survival saves, with the **same names as in the game's
load menu** (the file name, e.g. `sandbox1`, is shown underneath). Click the save you want to edit.

Only Survival saves are listed. Story and challenge saves are not supported.

### 3. Make your changes

| Tab | What you can do |
|---|---|
| **Overview** | Check the save: name, experience mode, region, days survived, condition |
| **Player** | Move the sliders for health, calories, thirst, fatigue and cold. For thirst, fatigue and cold, **0 = best**. *Restore all* sets everything to full. Raise skills (*Max* per skill, or *Max all skills*). Cure afflictions one by one or with *Cure all* |
| **Inventory** | Items are grouped by category like in the game. Select an item to change its **quantity** (stackable items like wood, matches, ammo) or its **condition**, or to **remove** it. Click **Add item…** to search the item list and add new items |
| **Raw JSON** | Read-only view of the whole save, for the curious |

The window title shows a `*` while you have unsaved changes. **Reload save** loads the open save again from
disk and discards unsaved edits (you're asked first). Use it if the game saved meanwhile, or after an error.

### 4. Save
Click **Save (with backup)**. The editor first copies the current save file to the backup folder (see below),
then writes your changes. The status bar at the bottom shows where the backup went.

### 5. Play
Start the game and load the save. Your changes are there.

---

## Why the game must be closed

The game keeps your save in memory while it runs:

- If you edit while the game is open and then go back to the menu and reload, the game **uses its old copy**
  and your changes don't appear.
- When the game saves again (sleeping, changing area, quitting), it **overwrites the file** and erases your edit.

So: quit the game completely, edit, save in the editor, then start the game.

The editor protects you from these mistakes:
- **Save** and **Restore** refuse to run while the game (`tld.exe`) is running. Your edits stay in the editor;
  quit the game and click Save again.
- If the save file changed or disappeared after you opened it, **Save stops**.  Use **Reload save** to load
  the current file before editing again.  File contents are checked even if the modification time was preserved.
- Saves are written to a unique temporary file, checked, and only then swapped in.  A failed write leaves
  the destination unchanged, and a destination change detected during staging prevents replacement.

---

## Where your saves are

The editor reads Survival saves from:

```
%LOCALAPPDATA%\Hinterland\TheLongDark\Survival\
```

That is `C:\Users\<your name>\AppData\Local\Hinterland\TheLongDark\Survival\`. Paste the first line into the
Windows Explorer address bar to open it. Each save is a file named `sandbox1`, `sandbox2`, ... (no extension).

If your saves are somewhere else, use **Open file…** in the editor to pick one.

## Backups and rolling back

All backups go to:

```
Documents\TldSaveEditor\Backups\
```

**Open backups folder** in the editor's toolbar opens it. Nothing is ever deleted there; clean the folder
yourself when you no longer need old backups.

| Backup | When | Name |
|---|---|---|
| Before edit | Every time you click **Save**, the file as it was **before** your change | `sandbox1_20261025_143000` |
| Manual | When you click **Backup now** | `sandbox1_20261025_143000_manual` |
| Before restore | When you restore a backup, the file it replaces | `sandbox1_20261025_143000_before-restore` |

A backup is never overwritten: if two are made in the same second, the second one gets `_2`.

### Quick backup before something risky

About to cross thin ice, fight a bear, or try something you might regret? Take a snapshot first:

1. Open the editor. The game can stay open.
2. Select your save in the list and click **Backup now**.

This only copies the save file and changes nothing. It copies **the game's last save**: the game writes the
file when you sleep, change area, or quit. If you want the backup to include your latest progress, trigger a
save in game first (e.g. pass time or go through a door), then click Backup now.

### Restoring a backup

1. Quit the game completely. The editor refuses to restore while the game is running, because the game
   would overwrite the restored file.
2. Click **Restore backup…**. The list shows every backup, newest first, with its type, its in-game name and
   when the game last saved it. Use the *Save* box to show only one save's backups.
3. Select a backup and click **Restore…**.
4. If the save file already exists, a **warning shows exactly which file will be overwritten**, with its
   in-game name and date. Nothing is replaced unless you click **Yes**.
5. The file being replaced is backed up first (*Before restore*), so a restore can be undone the same way.

**Different run warning:** backups are matched to saves by slot name (`sandbox1`). If you deleted a run in game
and started a new one, the new run can reuse the same slot. When a backup's in-game name differs from the save
it would replace, the editor shows a **DIFFERENT RUN** warning, so you don't replace your new run by mistake.

**Restoring by hand** (if you can't use the editor): quit the game, copy the backup into
`%LOCALAPPDATA%\Hinterland\TheLongDark\Survival\`, and rename it to the save name (e.g.
`sandbox1_20261025_143000_manual` → `sandbox1`), replacing the existing file.

**Tip:** before your first edit, also make your own copy of the save file somewhere safe.

## Good to know

- Some values are approximations. Item categories are guessed, since the save doesn't store them. Skill levels
  below 5 are only shown for Fire starting and Cooking.
- Items marked *story/challenge only* or *unverified name* in Add item were never seen in a Survival save and
  may not work. They are hidden unless you uncheck *Only items found in Survival saves*.
- Use at your own risk, and keep backups.

---
---

# For developers

Requires the .NET 10 SDK. Run from source:

```
dotnet run --project src/TldSaveEditor
```

Or open `TldSaveEditor.slnx` in Visual Studio.

## Projects

| Project | What it does |
|---|---|
| `src/TldSave.Core` | Save file library, no UI. `Lzf` (compression), `TldJson` (JSON with NaN/Infinity and nested JSON strings), `SaveFile` (load/save), `Inventory`, `PlayerState` (status, skills, afflictions), `ItemCatalog`, `SaveLocator` (find saves, backups) |
| `src/TldSaveEditor` | WPF app: save list, Overview, Player, Inventory, Raw JSON viewer |
| `tools/CatalogBuilder` | Generates `ItemCatalog.json` from the saves on this PC (see below) |
| `tests/TldSave.Core.Tests` | Automated tests on two real saves (see Testing) |

## Save format

```
file                 = LZF( JSON "SlotData" )         m_DisplayName, m_GameMode, m_Dict, ...
SlotData.m_Dict[key] = base64 -> LZF( JSON )          boot, global, info, screenshot, one per visited scene
inside sections      = many values are JSON strings containing more JSON (TldJson.ReadNested / WriteNested)
```

On save, only edited sections are re-encoded.  `SaveFile` keeps a SHA-256 fingerprint of each parsed section's
normalized JSON from load; sections whose JSON still matches keep the game's original bytes, including their
original whitespace and numeric spelling.  An edit to the player rewrites
only `global` (and `info` for condition), and all scenes stay as the game wrote them. Files are written to
an operation-specific temporary file, verified by loading them back, then swapped in with `File.Replace`.

## Player tab fields

| Value | Save field (in `global`) | Meaning |
|---|---|---|
| Condition | `m_Condition_Serialized.m_CurrentHPProxy` | 0–100, up to 105 with Well Fed. Also copied to `info.m_Condition`, which the load menu shows |
| Calories | `m_Hunger_Serialized.m_CurrentReserveCaloriesProxy` | Reserve kcal (max 2500), 0 = starving |
| Thirst / Fatigue / Freezing | `m_Thirst_Serialized`, `m_Fatigue_Serialized`, `m_Freezing_Serialized` | **0 = best**, 100 = worst (a brand-new game starts at ~0) |
| Skills | `m_SkillsManagerSerialized.m_Skill_*Serialized.m_Points` | Points. Level 5 = 200 fire, 350 cooking, 250 ice fishing, 125 archery, 350 mending, 100 for the others |
| Afflictions | `m_SprainedAnkleSerialized`, `m_BurnsSerialized`, ... | Wounds are parallel lists (cure = empty them); others use an `m_Active` flag |

## Item catalog ("Add item…")

`src/TldSave.Core/ItemCatalog.json` lists the items you can add. Notes, collectibles and keys are left out.
It is generated by `tools/CatalogBuilder`, which reads every save on this PC: the player inventory, plus the
containers and loose items of every visited scene. It can also read the original TS editor's item list.

For each item found in a save, a real sample becomes a "fresh item" template: full condition, stack of 1,
full calories, dry, not equipped. Position, ID and quest links are removed.

To regenerate it after playing more or after a game update:

```
dotnet run --project tools/CatalogBuilder -- src/TldSave.Core/ItemCatalog.json ../TLD-Save-Editor-3-main/src/tldSave/availableItems.ts
```

## Building the exe

```
dotnet publish src/TldSaveEditor -p:PublishProfile=win-x64
```

This produces a single self-contained `publish\win-x64\TldSaveEditor.exe` (~62 MB). When sharing, include
`THIRD_PARTY_NOTICES.md`: the original editor's MIT license requires its notice.

### Releases (GitHub Actions)

[.github/workflows/build.yml](.github/workflows/build.yml) builds and tests the solution on branch pushes,
pull requests, and manual runs.  Successful development runs attach an unsigned preview ZIP with the executable,
license, notices, README, and source/build details.  Test results and preview packages are retained for 14 days.

Pushing a version tag builds and tests that commit, then creates a GitHub Release with its versioned ZIP.
**Push the commit first, then the tag.**

```
git push origin master
git tag v1.0.1
git push origin v1.0.1
```

The tag sets the version (`v1.0.1` → 1.0.1).  To build a branch manually, open the Actions tab, select
*Build*, and click *Run workflow*.  The ZIP is attached to the run under *Artifacts*.

Actions are pinned to commit SHAs, and only the release job can write to the repository.
[.github/dependabot.yml](.github/dependabot.yml) checks for new action versions weekly and opens a single pull
request that updates the SHAs and their version comments. Review and merge it.

## Testing

```
dotnet test tests/TldSave.Core.Tests
```

`tests/TldSave.Core.Tests` (xUnit) covers LZF, JSON (NaN/Infinity, dates, depth limit, duplicate keys, complete documents),
save/load (only edited sections rewritten, crash-safe writes, changed-on-disk detection), inventory, player
state, the item catalog, and backup / restore (on temp folders, never your real ones). **Close the game before
running the tests:** the restore tests fail while `tld.exe` is running, like the real Restore button. They run on two real survival saves in `Fixtures/`: `sandbox1_GoDLC` (small) and
`sandbox3_1000Days` (13 MB, day 323). Both were checked for personal data: none, only game state.
Don't replace them unless the save format changes (see [Fixtures/README.md](tests/TldSave.Core.Tests/Fixtures/README.md)). Tests never
touch your real save folder; they work on copies in a temp folder.

The workflow runs the tests before packaging: **a failing test blocks the preview or release.**

### Upstream testing history

The following in-game results were reported by **YohanWD for the upstream editor**.  They do not establish
that this fork has been tested in the game.  This fork's status is recorded in [COMPATIBILITY.md](docs/COMPATIBILITY.md).

- Load and save were checked against 22 real saves: decoded data is identical after a round trip.
- Validated in game (2026-10-25) with [docs/TEST_SAVE_CHECKLIST.md](docs/TEST_SAVE_CHECKLIST.md): 66 added items
  of every component type, quantity / condition / remove edits, a game save, reload and re-edit. Player status
  meters, Well Fed and skill levels too.
- Validated in game on the October 2026 game update (changelist 188966), 2026-10-27: a pre-update save edited by
  the editor (14 added items, status, skills, an injected sprained wrist) loaded and was converted by the new
  version; the editor then read the game-written file, cured the sprain, and the game confirmed it was gone.
