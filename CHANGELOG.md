# HashDash — Change Log

## Version 2.0  (current)

### New Features
- **File selection column** — per-row checkbox in the DataGrid lets users choose exactly which files to hash; header checkbox selects or deselects all rows.
- **File size column** — displays each file's size using Windows Explorer-style adaptive formatting (KB / MB / GB). Populated instantly from OS metadata; no file content is read.
- **Source path display** — the path of the most recently browsed file or folder is shown inline in the controls bar.
- **Clear button** — removes all rows from the file list in a single action; disabled during hashing or when the list is empty.
- **XPS document export** — replaces the Print-to-PDF mechanism with a direct file-save workflow that produces a properly formatted, paginated XPS document.
- **Metadata in exports** — both XPS and CSV exports now include a header section containing Examiner Name, Lab Case Reference, Exhibit Reference, export date/time, program name, and program version.
- **Change Log viewer** — accessible via Help → Change Log; displays the full version history inside the application.

### UI and Layout
- **Windows 11 Fluent layout redesign** — clean five-row hierarchy (Header / Metadata / Controls / DataGrid / Status Bar) with logical grouping and consistent spacing.
- **Algorithm selector** — MD5 and SHA-256 checkboxes moved into a labelled group immediately adjacent to the Start Hashing button, communicating the "select → hash" workflow clearly.
- **Icon repositioning** — Screenshot icon placed before the Help icon in the top-right area; both icons are fully visible with correct padding.
- **Screenshot glyph updated** — changed to the Segoe MDL2 Assets screenshot glyph (E722) for a more accurate Fluent appearance.
- **DataGrid vertical alignment** — all cell content is vertically centred; row height and header height adjusted for Fluent-appropriate spacing.
- **DataGrid checkbox header** — header padding corrected so the Select All checkbox is fully visible and not clipped.
- **Hash columns** — MD5 and SHA-256 columns rendered in Consolas for legibility; widths sized to display full hash output without truncation.
- **Content-aware column sizing** — date columns sized for the "g" format; Size column right-aligned with a matching right-aligned header.
- **Status bar** — distinct background colour separates the action bar from the DataGrid; progress bar and Close button are grouped on the right; © credit moved beneath the Close button.

### Bug Fixes and Stability
- **Cancel button** — root cause of the repeated-cancellation crash fixed by scoping `CancellationTokenSource` to each individual hashing invocation (`using var cts`) and removing the token from the `Task.Run` second argument. Hashing can now be started, cancelled, and restarted any number of times without exceptions.
- **File size "0.00 MB"** — fixed by storing raw `FileInfo.Length` bytes and computing a formatted display string (`SizeDisplay`) that mirrors Windows Explorer's adaptive KB/MB/GB rules, including the "minimum 1 KB" floor.
- **Locked and inaccessible files** — `IOException` and `UnauthorizedAccessException` caught per-file; affected rows are marked FILE LOCKED or ACCESS DENIED rather than crashing the operation.
- **UI state recovery** — all controls are reliably re-enabled after hashing completes, is cancelled, or errors; `SetHashingState` centralises every state transition.
- **Screenshot during hashing** — Screenshot and Help icons are bound to `IsUiEnabled` and are disabled for the entire duration of a hashing operation, preventing concurrent access exceptions.

## Version 1.0  (initial release)

- Basic file and folder browsing.
- MD5 and SHA-256 hash calculation with progress indicator.
- CSV export and Print-to-PDF via Windows print dialog.
- Cancel button (single-use only).
- Examiner Name, Lab Case Reference, and Exhibit Reference metadata fields.
- Screenshot capture with auto-generated filename from metadata fields.
- Help icon with About and User Manual context menu.