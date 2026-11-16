# Changelog

All notable changes to AirClean are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the release workflow
publishes the matching section below as the GitHub release notes. Versions stay
`0.x` until the app is out of preview.

## [Unreleased]

### Added

- Keyboard coverage. `↑`/`↓` move between the category rows, `Space` toggles the focused
  row, `Enter` expands it, `Ctrl+,` opens settings, and `Esc` still backs out. The results
  page shows the shortcuts in the status bar, so they are discoverable without a manual.
- Screen-reader names on the controls that matter: scan, rescan, language, appearance,
  settings, the volume picker, the clean button, and every category, folder and file
  checkbox.

### Fixed

- Protected user folders are resolved through the known-folder API instead of
  `home + English name`. With OneDrive Backup moving Documents, the app scanned the
  redirected folder as content while no longer protecting it; the guard now follows the
  same folders the scan roots do. Downloads stays out of the list on purpose.

## [0.5.2] - 2026-11-16

A release-notes fix. The application binaries are unchanged from 0.5.1.

### Fixed

- GitHub releases now carry the changelog entry for their tag. Pushing a tag ran
  `generate_release_notes`, which lists merged pull requests; this repository commits
  straight to `main`, so every release body was a bare compare link (0.2.0 listed the
  same link four times).
- The release job extracts the `## [version]` section from this file and publishes it as
  the release body, keeping the "unsigned installers withheld" note that is appended
  afterwards. A tag without a section falls back to generated notes and warns in the run
  summary, so a missing entry is visible rather than silent.
- Every earlier release, 0.1.0 through 0.5.1, was backfilled with its section; 0.1.0 has
  no predecessor, so it carries no compare link.

## [0.5.1] - 2026-11-16

### Fixed

- File identity works on Apple silicon. macOS arm64 has only ever shipped the 64-bit
  inode ABI and exports that function as plain `stat`; `stat$INODE64` exists on Intel
  only, so every identity lookup returned "unsupported" and the delete check silently
  fell back to size and mtime. The provider tries the suffixed symbol first and falls
  back to the plain one on arm64 alone, because on Intel the plain symbol may be the
  legacy layout and reading an inode at the wrong offset is worse than reporting none.
  0.5.0 on macOS degraded to the older check; it did not delete anything extra.

## [0.5.0] - 2026-11-16

Correctness release: the app now proves the objects it deletes are the ones it scanned,
and stops promising space that deleting cannot free.

### Added

- File identity binding. Large-file and duplicate candidates record a stable identity
  (Windows volume serial plus the 128-bit `FILE_ID_INFO` file id; Unix `st_dev` plus
  `st_ino`), and the delete step re-reads it immediately before removing anything. A
  path that now points at a different object is skipped as *changed since scan*. Scanners
  only pay for the extra handle on items they actually offer, and an unsupported platform
  keeps the previous size/mtime check instead of blocking legitimate cleanups.
- Hard-link folding. Two paths to the same data used to look like two duplicate copies,
  so the window promised space that removing one of them could not free. Paths sharing an
  identity are folded, the folded count is reported ("N extra paths point at data already
  listed"), and the reclaimable total counts the data once. APFS and block clones have
  their own file id and stay ordinary copies; the scanner does not pretend otherwise.
- A read-only suggestion card. The Windows hibernation file, page file and component
  store are shown with their size (or an honest "size unknown"), one plain fact and a
  copyable command. They have no checkbox, never enter selection, the confirmation sheet
  or the deletion plan, and a probe that fails drops only its own row. A known size below
  200 MB is treated as noise and hidden; an unknown size is kept, because unknown is not
  zero.
- A `// SAFE:` source gate. Every `File.Delete`, `Directory.Delete`, `File.Move` and
  `Directory.Move` under `src/` must explain itself on the same line, and a test fails in
  CI naming each offending `file:line`. `docs/INCIDENTS.md` starts a ledger of what was
  actually broken, when, and which test now catches it.

## [0.4.3] - 2026-11-16

Trust fix: two places reported something precise that was not true.

### Fixed

- A damaged or unreadable `settings.json` no longer silently drops the excluded folders.
  Loading used to catch every failure and return defaults, so the next cleanup could
  delete folders the user had explicitly protected; a later save then wrote that empty
  list back. Settings loading now reports `Ok`, `FirstRun`, `RecoveredFromBackup`,
  `Corrupt` or `Unreadable`, keeps the previous good copy as `settings.json.bak`, and
  pauses cleaning behind a banner when neither copy is usable. Cleaning and emptying the
  Trash become no-ops while paused, so a key binding cannot bypass it, and a broken file
  is never promoted over the last good backup.
- Folders that could not be read are no longer counted as "nothing there". The scan had
  collected the permission-skip count all along but never showed it, so a machine without
  full disk access reported a tidy, exact-looking total that was too small. The results
  and completion views now say how many folders could not be read, and every total is
  formatted as a lower bound (`12.4 GB+`) when it is one.

## [0.4.2] - 2026-11-11

### Fixed

- The cleaning spinner no longer orbits its container or covers the title. Rotating the
  `Arc` control and then a transformed panel both moved the arc's visual origin; the 100°
  arc is now rebuilt inside a fixed 18×18 box on every frame, so the centre stays at
  (9,9) for the whole animation.

## [0.4.1] - 2026-11-11

### Fixed

- Cleanup progress no longer keeps the previous batch's numbers. Cancelling a cleanup and
  then emptying only the Trash could show the earlier percentage and deleting count; all
  progress fields are reset and notified when a batch starts and when it ends.

## [0.4.0] - 2026-11-11

### Added

- A settings window with appearance, scanning and behaviour sections. Controls edit a
  draft, **Save** writes it back and applies it, **Cancel** changes nothing.
- Quick and full scans. Quick covers caches, logs and the Trash; full adds large files and
  duplicates. A data drive in quick mode still lists large files, because a trash-only
  scan would not be worth the trip.
- Excluded folders, honoured by every scanner (nothing is walked) and by the deleter
  (nothing is removed), with a reason recorded when an item is refused.
- Configurable thresholds: large-file size and list cap, smallest duplicate and group
  cap, and the hashing budget; the MFT fast path and the elevated helper respect the caps
  too.
- A tray icon with open, scan now and exit, plus optional close-to-tray and
  minimize-to-tray. Tray creation falls back silently where StatusNotifier is missing.
- Reminders (off by default; they only report, never scan or delete) and start-at-login on
  all three platforms.
- Delete-failure protection: a batch aborts when too many items fail, a full disk is
  detected and reported instead of retried per file, and the completion page explains if
  the run stopped early.

### Changed

- Settings schema v2: scan scope, exclusions, thresholds, updates, tray and reminders are
  stored, migrated from v1, and clamped on load.

## [0.3.0] - 2026-11-10

### Added

- Undo the last cleanup on all three platforms: Windows parses the recycle bin's `$I`
  records, Linux reads `.trashinfo`, and macOS restores from the journal.
- Recent cleanups on the welcome screen, restorable from the same shelf.
- A skip report grouped by reason, expandable, with CSV export; truncated group lists say
  how many more there were.
- Empty the Trash from the completion page, with a second confirmation, after which undo
  disappears as it should.
- A crash log for unhandled exceptions on any thread, and single-instance detection that
  focuses the window that is already running.
- Cancelling a scan keeps the categories that had already finished.

### Changed

- The journal is per batch, readable, pruned by age and count, and still reads v1 files.

## [0.2.0] - 2026-11-02

### Added

- An NTFS MFT fast path for large-file scans: reading the master file table is roughly an
  order of magnitude faster than walking directories, needs administrator rights, and
  falls back to the directory walk without them.
- An administrator helper. When enabled, the app starts a controlled `--admin-helper`
  child over UAC and receives MFT results over a named pipe; the main process never
  elevates, and the helper accepts only a volume root or a fixed rule id, never an
  arbitrary path list.
- Administrator-only cleanups for the Windows Update cache, the Linux apt package cache,
  and systemd journal logs, each labelled separately in the confirmation sheet. Logs keep
  the last 14 days and stay under 500 MB.
- Live and final scan elapsed time.

### Changed

- Release builds are compiled ahead of time on all three platforms, and both the publish
  step and every pull request fail on an AOT or trimming warning rather than shipping a
  build whose reflection paths were removed.
- Releases no longer offer the Windows Setup or macOS package while the builds are
  unsigned; portable archives, nupkg files and update manifests remain.

## [0.1.0] - 2026-10-31

First public preview.

### Added

- A cross-platform Avalonia shell with a warm-paper light palette and a dark theme, and
  English and Simplified Chinese interfaces that switch live.
- One scan per volume that covers cache rules, large files, the Trash and duplicates, with
  categories appearing as they finish instead of waiting for the slowest.
- Tri-state category selection, per-folder aggregation and per-item opt-out, with nothing
  selected by default for large files and duplicates.
- Cleanup with progress and cancellation, the Trash by default, permanent deletion only
  when explicitly chosen, and a JSON journal per batch.
- Velopack installers for Windows and macOS plus portable packages, and an MIT licence.
- CI that builds and tests on Windows, macOS and Linux.

### Known limitations

- Installers are unsigned, so SmartScreen and Gatekeeper warn on first launch.
- There is no undo, no recent-cleanup list and no skip report yet.
- macOS and Linux packages are produced by CI, but on-device verification was still
  pending at this release.
