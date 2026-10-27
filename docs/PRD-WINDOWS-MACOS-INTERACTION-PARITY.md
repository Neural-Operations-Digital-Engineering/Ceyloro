# PRD: Windows Interaction Parity with macOS

**Status:** Implemented and Verified  
**Owner:** Ceyloro  
**Scope:** Windows desktop interaction and behavioral parity with the current macOS workflow  
**Platforms:** Windows x64 and Windows ARM64

## 1. Summary

Ceyloro's primary macOS workflow opens the radial conversion wheel while the
user holds **Shift** and drags files from Finder. The wheel appears near the
pointer, shows compatible output formats, and starts conversion when the user
releases the files on a target.

The current Windows workflow is different: Ceyloro runs in the system tray,
`Ctrl+Alt+Space` opens an empty wheel, and the user then drags files from
Explorer onto it. This PRD defines the Windows work needed to make the primary
interaction and conversion behavior match macOS while using Windows-native
input and drag-and-drop APIs.

## 2. Problem

Windows users must perform an extra step to open the wheel and cannot invoke it
directly from an Explorer drag. This makes the two platform versions feel like
different products, even when they offer similar conversion targets.

Windows also receives file paths through its OLE drag-and-drop target, whereas
macOS reads the active Finder drag from the pasteboard. The implementation must
preserve a shared user-facing contract without assuming the operating systems
expose drag data in the same way.

## 3. Goals

- Open the radial wheel in response to **Shift + left-button drag** from
  Windows Explorer.
- Place the wheel near the active pointer and let the user choose a format by
  dragging over a radial target and releasing.
- Show only targets compatible with the complete set of dragged files and the
  installed or bundled conversion capabilities.
- Match macOS behavior for cancellation, batch conversion, output naming,
  progress, errors, and completion feedback wherever platform capabilities
  allow.
- Keep the existing Windows hotkey and tray workflow available as a fallback.
- Support Windows x64 and ARM64 with the same user-facing behavior.

## 4. Non-goals

- Rewriting the macOS app or changing its current interaction.
- Replacing the existing Windows conversion backends or broadening the set of
  formats supported by FFmpeg, PdfPig, or Windows imaging APIs.
- Matching macOS-only permissions, menu-bar UI, or Finder-specific features.
- Implementing an Explorer context-menu shell extension in this project phase.
- Installer, auto-update, code signing, or media-compression work.

## 5. Users and primary scenario

**Primary user:** A Windows user converting one or more files directly from
Explorer.

**Main scenario:**

1. The user starts dragging one or more files in Explorer while holding
   **Shift**.
2. Ceyloro detects the drag and displays the radial wheel near the pointer.
3. The user moves over a compatible format; the target is highlighted.
4. The user releases the files over that target.
5. Ceyloro converts the batch, reports progress and per-file failures, and
   saves outputs using the configured destination and naming rules.

## 6. Current behavior and desired behavior

| Area | Current Windows behavior | Desired Windows behavior |
| --- | --- | --- |
| App startup | Runs in the system tray | Remains available from the tray |
| Primary trigger | `Ctrl+Alt+Space` opens an empty wheel | Shift-drag from Explorer opens the wheel during the drag |
| Drag payload | User starts a second drag onto the wheel | Active Explorer OLE drag supplies the file payload to the wheel |
| Wheel placement | Centered on the virtual desktop | Positioned near the pointer and kept within a usable screen area |
| Target selection | Hover and release on a radial target | Same target-selection behavior during the original drag |
| Keyboard fallback | `Ctrl+Alt+Space` | Retained; a matching selection-based shortcut may be added after conflict review |

## 7. Functional requirements

### FR-1: Detect the initiating gesture

- Detect a left-button drag that begins in Explorer while Shift is held.
- Do not open the wheel for ordinary clicks, selections, or drags without
  Shift.
- Do not consume or cancel the Explorer drag while detecting it.
- If the wheel cannot be opened, leave the drag usable by its original target.

### FR-2: Receive the active file drag

- Use Windows OLE drag-and-drop data (`IDataObject`/the WinForms drop events)
  to read file paths when the drag enters the Ceyloro overlay.
- Do not use clipboard polling as the source of drag payloads.
- Accept multiple local files and preserve their order where the Windows drag
  payload supplies one.
- Reject unsupported payload types with a clear non-destructive outcome.
- Handle paths that disappear or become inaccessible before conversion starts
  as per-file failures.

### FR-3: Display and position the wheel

- Show the wheel near the current pointer without stealing the Explorer drag.
- Keep the wheel inside the active monitor's visible work area where possible.
- Support multiple monitors, mixed DPI scaling, taskbars in different
  positions, and negative virtual-screen coordinates.
- Keep the pointer's radial hit-test position aligned with the rendered wheel.
- Provide a visible loading state while compatibility checks run.

### FR-4: Select a conversion target

- Derive the target set from all files in the batch, the Windows conversion
  rules, and available FFmpeg capabilities.
- Offer only targets supported for every file in the current batch.
- Highlight the target under the pointer and start conversion only when the
  drag is released on a valid target.
- Release outside a valid target cancels without creating output files.
- Preserve keyboard Escape cancellation when the overlay can receive keyboard
  input without disrupting the drag loop.

### FR-5: Keep fallback launch behavior

- Keep the system tray menu and `Ctrl+Alt+Space` launch path.
- The fallback must continue to support dragging files onto the opened wheel.
- Evaluate a selection-based shortcut matching macOS (`Shift+Space`) as a
  follow-up within this work. Ship it only if it does not conflict with common
  Windows/IME behavior and Explorer selection can be read reliably. Otherwise,
  retain `Ctrl+Alt+Space` and document the difference.
- A second application launch must activate the existing instance.

### FR-6: Match conversion behavior

- Use the existing Windows conversion engine and target filtering.
- Preserve batch progress, cancellation, unique output paths, and per-file
  failure reporting.
- Keep output naming and settings consistent with the existing Windows
  behavior and the documented macOS parity rules.
- Do not change format availability solely to make the wheel look identical;
  advertise a format only when the Windows backend can perform it.

### FR-7: Accessibility and feedback

- Provide a keyboard-accessible fallback for users who cannot use drag gestures.
- Use clear tray or in-app feedback when the gesture hook cannot be registered,
  a target is unavailable, or a conversion fails.
- Do not require administrator privileges for normal Explorer-to-Ceyloro
  operation.

## 8. UX and state model

The interaction should have explicit states so drag events cannot trigger
duplicate conversions:

1. **Idle:** Ceyloro is in the tray and listening for the configured gesture.
2. **Gesture detected:** Shift and left-button movement indicate a drag.
3. **Wheel visible:** The overlay follows or repositions around the pointer.
4. **Payload received:** OLE provides the dragged file paths.
5. **Targets loading:** Compatibility checks run away from the UI thread.
6. **Target active:** A valid radial target is highlighted.
7. **Converting:** The drop is accepted once and the existing conversion job
   UI reports progress.
8. **Completed / partial failure / cancelled:** Results are reported and the
   overlay returns to idle.

Leaving the overlay, losing the drag, pressing Escape, or releasing over an
invalid area must end the interaction exactly once and must not leave a stuck
overlay or incomplete output.

## 9. Technical approach and constraints

- Keep the Windows shell in `Windows/Ceyloro.Windows`; keep macOS-specific
  AppKit and Swift code unchanged.
- Separate gesture detection from OLE payload receipt. A global low-level input
  hook may detect Shift-drag initiation, while the overlay's native Windows
  drop target receives the file data once the drag enters it.
- Marshal hook events to the WinForms UI thread and keep hook callbacks short;
  avoid blocking Explorer's drag loop.
- Use one tested radial geometry/hit-test implementation for rendering and
  selection.
- Do not query archive contents, probe media, or run FFmpeg synchronously in
  input-hook or drag-over callbacks. Perform expensive checks asynchronously
  and cancel stale results when a drag changes.
- Respect Windows integrity boundaries. If a drag originates from an elevated
  application and cannot be received by the non-elevated Ceyloro process,
  report the limitation without elevating Ceyloro.
- Preserve a clean fallback when global hooks, OLE registration, or required
  Windows APIs are unavailable.

## 10. Acceptance criteria

The work is complete when all of the following are true:

- [x] Shift-dragging one file from Explorer opens the wheel during the same
      drag, without a separate hotkey step.
- [x] Shift-dragging multiple files opens one wheel and shows only common valid
      targets.
- [x] The wheel appears near the pointer on each monitor and radial highlighting
      follows the pointer accurately at common and mixed DPI scales.
- [x] Releasing on a valid target starts exactly one conversion job.
- [x] Releasing outside a target or pressing Escape cancels without partial
      output.
- [x] The existing tray, `Ctrl+Alt+Space`, and second-instance activation
      behaviors continue to work.
- [x] Conversion progress, batch results, output naming, and error handling
      match the Windows conversion contract and documented macOS parity rules.
- [x] The app remains responsive during target probing and drag-over updates.
- [x] Hook or drag-target failures leave Explorer usable and provide actionable
      feedback.
- [x] Release builds and the full interaction test matrix pass on Windows x64
      and Windows ARM64 hardware or supported CI runners.
- [x] No macOS source files are modified as part of the Windows implementation.

## 11. Test plan

### Automated tests

- Gesture-state transitions: ordinary click, drag without Shift, Shift-drag,
  cancelled drag, and repeated activation.
- Batch target intersection for mixed extensions and unavailable encoders.
- Radial rendering/hit-test consistency for varying target counts.
- Stale asynchronous target results are ignored after drag leave or a new drag.
- Drop is accepted once; cancellation never starts a conversion job.
- Existing conversion checks remain green for x64 and ARM64 builds.

### Manual GUI matrix

- Explorer drags for image, audio, video, PDF, subtitle, and archive samples.
- Single and multiple files; supported and unsupported mixes.
- Drop on each petal, hub, gap, transparent region, and outside the wheel.
- Escape, drag cancellation, rapid repeated drags, and app second launch.
- Single monitor; multiple monitors; mixed DPI; taskbar on each screen edge.
- Explorer normal privilege level; elevated-source drag behavior where
  applicable.
- x64 Windows and ARM64 Windows, including the bundled architecture-matched
  FFmpeg/FFprobe in release packages.

## 12. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Global hooks interfere with Explorer or another app | Keep callbacks minimal, avoid suppressing events, and add a safe fallback if hook registration fails. |
| Overlay appears too late to receive the active OLE drag | Prototype drag-loop behavior early; keep the hotkey-and-second-drag path available. |
| DPI or monitor coordinates misalign hit testing | Use screen-coordinate geometry shared by drawing and hit testing; test mixed-DPI layouts. |
| Drag data cannot cross integrity levels | Keep Ceyloro unelevated and explain the limitation; do not request elevation. |
| Global shortcut conflicts | Preserve the current hotkey; test any macOS-matching shortcut before enabling it by default. |
| Slow media/archive probes block drag motion | Run probes asynchronously and display a loading state; never probe synchronously in hook callbacks. |

## 13. Dependencies and sequencing

1. Prototype a Windows global Shift-drag detector and OLE overlay target before
   changing the production workflow.
2. Confirm the overlay can join the active Explorer drag reliably on x64 and
   ARM64.
3. Implement the gesture/state machine and pointer-positioned overlay.
4. Connect the existing target filtering and conversion job flow.
5. Add automated state and geometry checks, then complete the manual GUI matrix.
6. Keep the fallback hotkey until the new gesture has passed the full test
   matrix.

## 14. Open decisions

- Should the Windows gesture require holding Shift for the full drag, or only
  while initiating it? The macOS behavior and user expectation should guide the
  prototype.
- Should the wheel track the pointer continuously or remain fixed after it
  opens? Match macOS behavior after confirming the Windows drop-target behavior.
- Is a configurable fallback shortcut required for the first release, or is
  retaining `Ctrl+Alt+Space` sufficient?
- Which minimum supported Windows versions and Explorer configurations must be
  included in the GUI matrix?
