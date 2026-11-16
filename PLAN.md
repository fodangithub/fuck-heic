# PhotoViewer — Toolbar + Debug HTTP Service Plan (2026-11-16)

Repo: `F:\tmp\PhotoViewer` (pushes to github.com/fodangithub/fuck-heic, SSH).
Existing: C# WPF .NET 9, folder-publish, libheif HEIC decode, multithread decode, neighbor preloader.

## Feature 1 — Bottom-center toolbar (MainWindow.xaml, code-behind)
Layout rows: 0=image, 1=toolbar, 2=status bar. Dark theme styles (ToolButton/ToolTextBox/ToolCombo).
- **Nav group:** Prev, Next, Play/Pause toggle, "Interval (s)" TextBox (default 2.5, persisted).
- **Zoom group:** Zoom−, Zoom+, editable zoom% ComboBox (presets 25/50/100/200/400%, Enter to apply typed value), Fit window, Original(100%), Reset (pos+size+rotation).

## Feature 2 — Settings persistence (AppSettings.cs) ✅ created
- `AppSettings` JSON at `%AppData%\PhotoViewer\settings.json`.
- Fields: `AutoPlayIntervalSeconds` (2.5), `DebugHttpPort` (8321).
- `Load()` tolerant of missing/corrupt file; `Save()` best-effort; `IntervalSecondsClamped` (0.1–3600).

## Feature 3 — Code-behind wiring (MainWindow.xaml.cs)
- Fields: `_settings`, `_autoPlayTimer`, `_updatingZoomUi`, `DebugPort`.
- Ctor: load settings → IntervalBox.Text; timer.Tick→Next(); IntervalBox LostFocus/Enter→ApplyIntervalFromBox().
- Handlers: Prev/Next/Play, ZoomIn/Out (center-anchored), ZoomBox SelectionChanged + PreviewKeyDown(Enter),
  Fit/Original/Reset buttons.
- `SetZoom` also updates ZoomBox text (guard `_updatingZoomUi` to avoid re-entrancy).
- Public API for debug service:
  `IsAutoPlaying`, `AutoPlayIntervalSeconds`, `StartAutoPlay(double?)`, `StopAutoPlay()`,
  `NavigateNext()`, `NavigatePrevious()`, `PanBy(dx,dy)`, `ZoomToScale(scale)`, `ZoomStep(factor)`,
  `FitWindow()`, `ShowOriginal()`, `ResetView()`, `RotateBy(deg)`, `GetDebugInfo()`.
- Space key toggles auto-play (add to PreviewKeyDown).

## Feature 4 — Debug HTTP service (DebugHttpService.cs) `#if DEBUG`
- Minimal HTTP over **TcpListener on 127.0.0.1** (avoids HttpListener URL-ACL/admin issues).
- Accept loop on background thread; parse request line + query; dispatch via Dispatcher.InvokeAsync to UI thread.
- Endpoints (GET/POST):
  - `GET /info` → image pixel size, viewport size, image-element size, zoom, zoomPercent, rotation, pan, file, index, count, playing, interval.
  - `POST /pan?dx=&dy=` ; `/pan/left|right|up|down?n=`
  - `POST /zoom?scale=X` ; `/zoom/in` ; `/zoom/out`
  - `POST /nav/next` ; `/nav/prev`
  - `POST /fit` ; `/original` ; `/reset` ; `/rotate?deg=`
  - `POST /play?interval=` ; `/pause`
- Start in `App.OnStartup` under `#if DEBUG`; dispose in `OnExit`.

## Feature 5 — Tests (tests/ui_debug_tests.py)
Python (urllib) hitting a **running Debug build** on port (default 8321). Assertions:
1. `/info` → 200, image pixel size > 0, viewport size > 0.
2. `/zoom?scale=2` → zoom≈2.0 and image-element width ≈ pixelWidth×2.
3. `/pan?dx=-50` → pan.x -= 50.
4. `/pan/left?n=25` → pan.x -= 25.
5. `/fit` → pan≈0, zoom==fitZoom (<=1).
6. `/original` → zoom==1.0.
7. `/nav/next` → index+1 (mod count), file changed.
8. `/reset` → rotation==0, pan≈0, zoom==fitZoom.
9. `/rotate?deg=90` → rotation==90.

## Execution order
1. AppSettings.cs (done) → 2. XAML toolbar (done) → 3. code-behind wiring → 4. DebugHttpService → 5. App startup hook →
6. build Debug+Release → 7. run Debug build with a HEIC → 8. run tests, fix → 9. publish Release → 10. commit+push.

## Notes / gotchas
- Don't write deliverables into F:\Trae workspace; publish to F:\tmp\PhotoViewer\publish.
- Shell cwd resets between Bash calls — always chain `cd`.
- Keep `libheif.dll` beside exe (CopyNativeLibheif MSBuild target handles it).
- Release publish must NOT include the debug HTTP service (`#if DEBUG` strips it).
