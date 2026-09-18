# Page-backed navigation refactor

Branch: `page-backed-navigation`. Started 2026-09-17. **Code complete 2026-09-17; awaiting the F5
walk-through below.**

The reasoning that outlives this document now lives in `CLAUDE.md` — *Architecture*, *Appearance
state*, and the Per-file notes for `Views/MainPage.Navigation.cs`, `Services/AppearanceService.cs`
and `Views/IShellContentPage.cs`. This file is the record of the change itself.

## Why

Switching to Games or Profile showed the *previously visited* page-backed view for a few frames
before the real one appeared. Measured off a 25fps screen recording: the header, the nav-pane
selection and the Frame's visibility all flipped on one frame, and `ContentFrame` then rendered the
**old page at full opacity for five frames — 200ms** — before the new one landed.

Root cause was architectural, not a bug in any one method. `ContentFrame` was shared by two nav
destinations and toggled with `Visibility`:

- `ShowInlinePanel` only *collapsed* the Frame. It never cleared it, so the Frame kept whichever
  page-backed view was last visited.
- A `Collapsed` element is not measured or arranged, so while the Frame was hidden the new page
  could not lay out.
- Revealing the Frame therefore exposed the stale page and *then* spent several layout passes
  catching up.

It only reproduced with an inline panel in between (Home ▸ Games straight after Games ▸ Profile was
fine, and the first visit had an empty Frame), which is what made it look intermittent.

## Three fixes that did not work — do not retry these

1. **Navigate before revealing.** `Frame.Navigate` sets `Content` synchronously but does *not* swap
   the rendered visual synchronously. Reordering the two statements changes nothing.
2. **Gate the reveal on `page.IsLoaded` / the page's `Loaded` event.** `IsLoaded` describes the page
   *object*, not what the Frame is presenting. `GamesPage` is `NavigationCacheMode.Required`, so a
   return visit hands back an already-loaded instance, `IsLoaded` is true immediately, and the gate
   released while the Frame still showed the old page. `ActualHeight > 0` fails the same way — a
   cached page keeps its last arranged size while out of the tree. `VisualTreeHelper.GetParent(page)
   != null` *is* a correct "is it presented" test and reported `presented=True`, and the flash still
   happened.
3. **Gate with `ContentFrame.Opacity = 0` plus a composition entrance animation on the Frame.**
   `AnimationBuilder` animates the element's composition visual, and once `Visual.Opacity` is owned
   by a composition animation the XAML `UIElement.Opacity` property no longer drives what is
   rendered — so the gate became a no-op from the second navigation on and the flash got *longer*.
   The tell in the recording was the stale page visibly **sliding**.

The lesson: as long as one `Frame` is hidden and re-shown while holding another destination's page,
something stale is on screen at reveal time. The fix is to stop hiding the Frame.

## What was built

Every nav destination is a `Page` in `ContentFrame`. The Frame is created visible and is never
collapsed, never opacity-gated. All transitions are the platform's own `NavigationThemeTransition`.

Gone: `ContentScrollViewer` / `ContentPanel`, `ShowInlinePanel`, `ShowProfilePage`, `ShowGamesPage`,
`RevealContentFrame`, `OnRevealRendering`, `CompletePendingReveal`, `CancelPendingReveal`,
`MaxRevealFrames`, `PlayPanelEnterAnimation` and `UpdateContentPadding`.

`ShowContent(tag)` is now: set the header, remember the tag, name the Main landmark,
`ContentFrame.Navigate(PageTypeFor(tag))` unless it is already there, `TrimContentBackStack()`, name
the content region, optionally move focus.

### New files

| File | What |
|---|---|
| `Views/HomePage.xaml(.cs)` | the former `LatestNewsPanel`, plus all of `MainPage.Favorites.cs` |
| `Views/BetasPage.xaml(.cs)` | the former `BetasPanel` |
| `Views/SocialPage.xaml(.cs)` | the former `SocialPanel` |
| `Views/SettingsPage.xaml(.cs)` | the former `SettingsPanel`, plus `MainPage.Settings.cs` (bar the welcome dialog) |
| `Views/SettingsPage.Appearance.cs` | the control half of `MainPage.Appearance.cs` |
| `Views/SettingsPage.Accent.cs` | all of `MainPage.Accent.cs` |
| `Views/IShellContentPage.cs` | `ContentRegion` — the element the shell names and focuses |
| `Services/AppearanceService.cs` | the painting half of `MainPage.Appearance.cs`, plus the state behind it |

Deleted: `Views/MainPage.Accent.cs`, `Views/MainPage.Favorites.cs`. `MainPage.xaml` went from 1229
lines to 167.

### How the `MainPage.Appearance.cs` split was settled

It was doing two jobs and only one of them belonged to a page. `AppearanceService` (static, like
every other service) owns the state — theme, tint tag, body/pane acrylic opacity, tint scope — the
`LocalSettings` keys behind it, and every surface it paints: the one `HostBackdrop` `AcrylicBrush`
and the two pane brushes in `App.xaml`'s `ThemeDictionaries`. `SettingsPage` owns only the controls.

Two decisions worth knowing:

- **The shell does not need a repaint callback for the surface.** It assigns
  `RootGrid.Background = AppearanceService.SurfaceBrush` once in its constructor; the service mutates
  that instance's dependency properties thereafter. The `Changed` event exists for the two things
  that are *not* that brush — the title bar (`ApplicationView`, shell-owned) and Settings' own
  swatches and legibility warning.
- **Persistence became a parameter (`SetTint(tag, persist)`) rather than an ambient
  `_loadingSettings` flag.** The flag could not survive the split: it was a page field the painting
  code read. It now guards only `SettingsPage`'s own handlers, where its initial `true` is still
  load-bearing for the `Slider.Minimum` coercion during `InitializeComponent`.

### Decisions the plan left open

- **Back navigation.** `TrimContentBackStack` stayed, and `CanGoBackInPlace()` still reports true
  only for `ContentFrame.Content is LoginPage`. Making every destination page-backed did not change
  the reasoning — pane destinations are siblings, not a stack — only the number of page types it
  applies to. The `ContentFrame.Visibility` check in `CanGoBackInPlace` is gone, since the Frame is
  always visible now.
- **Padding.** Each content page carries the `WindowWidthStates` `AdaptiveTrigger` pair that
  `GamesPage` already used (12 under 641epx, 24 above), so the shell pads nothing.
- **Caching.** `SettingsPage` is `NavigationCacheMode.Required` (heaviest page, always one click
  away); `HomePage` is `Required` too; Betas and Social are left alone.

## Gotchas that applied

- **The csproj is old-style with explicit item lists.** Adding a file is always a two-part change:
  the file *and* the `<Compile>` / `<Page>` entry, with `<DependentUpon>` for partials.
- **`x:Uid` values moved with their markup and the resw keys did not change.** The panel markup was
  extracted by line range and dedented mechanically rather than retyped, so no `x:Uid` could be
  mistyped — but a mistyped one renders blank with no error, so check every moved panel on screen.
- **Repo is CRLF.** Every new and modified file was normalised to CRLF after writing.
- **No comments in code.** CLAUDE.md's *Per-file notes* were restructured in the same change: every
  moved bullet travelled with its code, the reveal-gate bullets were consolidated into one
  "do not retry these" note, and `MainPage.Accent.cs` / `MainPage.Favorites.cs` headings became
  `SettingsPage.Accent.cs` / `HomePage.xaml.cs`.
- `LangVersion` is 7.3.

## Verification

Command-line MSBuild cannot confirm any of this — it compiles but does not deploy.

- ✅ `/t:Rebuild` Debug x64 with packaging off: exit 0, **no `CS`, `XLS` or `WMC` lines at all**.
- ✅ `/t:Build` **Release** x64 (the ILC / .NET Native toolchain CI uses, and the only configuration
  that emits the *static* `XamlTypeInfo` table for the four new pages): exit 0, clean.
- ✅ The moved markup was extracted by line range and dedented, never retyped, and diffed back
  against `HEAD` ignoring whitespace: all four panels and all three `DataTemplate`s are
  content-identical, so no `x:Uid` or handler name can have been mistyped in the move.
- ⬜ **F5 in Visual Studio** and walk: Home → Games → Home → Profile → Home → Games → Settings →
  Profile.

Success is: no frame of any transition shows the previous destination's content. If anything still
flashes, record a GIF and frame-difference it (`System.Drawing` reads GIF frames; no ffmpeg needed) —
that is how this was diagnosed and it is faster than reasoning about it.

Worth checking on the same run, because none of it can be verified from a build:

- Every moved panel renders its text (a mistyped `x:Uid` is blank, silently).
- Settings: theme radios, tint swatches (including the checkmark on Default), the custom tint
  dialog's live preview, both transparency sliders and their warning, the tint-scope radios, the
  accent row and its restart notice.
- The theme switch repaints the title bar caption buttons and Settings' swatches while Settings is
  on screen.
- Reset app data → the appearance controls come back to defaults *and* the surfaces match them.
- Home's favourites: star from Games, come back to Home, the row is there.
- Keyboard: a nav gesture moves focus into the content region; arrow keys scroll it.
