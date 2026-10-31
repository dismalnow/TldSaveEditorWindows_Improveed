# Compatibility and verification

## Status of 1.3.0-preview.1

This fork starts from YohanWD's v1.2.1 source commit, `3dc5b5dbcb76fd328cd025200fed448ad1d24b08`.
Its first changes focus on preserving unedited data, rejecting invalid edits, and preventing stale writes.

Local verification on 2026-10-31 used .NET SDK 10.0.403: **112 tests passed**, with no failures or skipped tests.
The complete Release solution compiled with Windows targeting enabled, with zero warnings and zero errors.
This was a Linux build host, so it verifies compilation of the WPF application without exercising its Windows UI.

| Evidence | What it establishes |
|---|---|
| Automated core tests | Expected behavior against the two public upstream fixtures and synthetic edge cases |
| Windows build and packaging workflow | The tested source compiles and produces a Windows executable |
| Upstream author's October 27 test | Upstream v1.2.1 was reported working with game changelist 188966 |
| This fork in Steam Survival 2.59, build 189140 | **Pending an in-game test.**  Automated tests are not evidence of this result |

Hinterland's [October 29/30 Steam hotfix announcement](https://hinterlandforums.com/forums/topic/48698-the-long-dark-iron-road-updated-to-version-259-priority-fixes/?comment=310760&do=findComment)
identifies Survival 2.59, build 189140.  That post gives separate versions for Episode Five and WINTERMUTE;
neither mode is supported by this editor.  Challenge saves are also unsupported.

The upstream [compatibility update](https://github.com/YohanWD/TldSaveEditorWindows/compare/v1.2.0...v1.2.1)
describes the author's changelist 188966 test.  It predates that hotfix and is not a claim of in-game
validation for this fork.  Check the game version again when testing a later game update.

## Required in-game acceptance check

Use a disposable Survival run or a copy of a run that has an independent backup.  Close the game completely
before an editor write.  Record the following alongside the result:

- Game version/build, storefront, and installed DLC.
- Editor commit/build, save type, and whether the save was new or upgraded by the game.
- The specific changes made and unrelated values inspected afterward.
- The game's load, play, save, exit, reload result, plus the editor's ability to reopen that game-written file.

Minimum sequence:

1. Create a backup outside the active save folder and record its filename.
2. Open a disposable run and save without edits.  Load it in the game and inspect inventory, water/fuel,
   location, and player status.
3. Change one ordinary inventory quantity.  Load it in the game and verify the requested change.
4. Trigger a game save, exit, reload in the game, then exit and reopen the file in the editor.
5. Restore the independent backup and verify the original run loads.

Test additional capabilities separately before claiming them: adding items, changing skills, curing
afflictions, and modifying player meters.  The upstream [detailed checklist](TEST_SAVE_CHECKLIST.md) can guide
that work; its existing marks are historical upstream notes, not completed tests of this fork.

## Regression coverage added by this fork

- Untouched encoded sections survive alternate JSON formatting and number spelling.
- Same-timestamp file replacements, deleted source files, and destination changes during staging stop a write.
- Concurrent/stale temporary files are not reused or deleted by another write operation.
- Invalid save structure, invalid UTF-8, duplicate JSON properties, and extra JSON roots are rejected.
- Numeric edit APIs reject non-finite inputs without changing loaded state.
- Maxing available skills does not create missing or unsupported skill structures.

These checks preserve existing unfamiliar data.  They do not add support for untested game mechanics,
new item templates, or unsupported game modes.
