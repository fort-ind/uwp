# Page-backed navigation refactor

Branch: `page-backed-navigation`. Started 2026-09-17.

## Why

Switching to Games or Profile shows the *previously visited* page-backed view for a few frames
before the real one appears. Measured off a 25fps screen recording: the header, the nav-pane
selection and the Frame's visibility all flip on one frame, and `ContentFrame` then renders the
**old page at full opacity for five frames — 200ms** — before the new one lands.

Root cause is architectural, not a bug in any one method. `ContentFrame` is shared by two nav
destinations and is toggled with `Visibility`:

- `ShowInlinePanel` only *collapses* the Frame. It never clears it, so the Frame keeps whichever
  page-backed view was last visited.
- A `Collapsed` element is not measured or arranged, so while the Frame is hidden the new page
  cannot lay out.
- Revealing the Frame therefore exposes the stale page and *then* spends several layout passes
  catching up.

It only reproduces with an inline panel in between (Home ▸ Games straight after Games ▸ Profile is
fine, and the first visit has an empty Frame), which is what made it look intermittent.

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

## Target architecture

Every nav destination becomes a `Page` in `ContentFrame`. The Frame is created visible and is never
collapsed, never opacity-gated. All transitions are the platform's own `NavigationThemeTransition`.

- No `ContentScrollViewer` / `ContentPanel` inline host.
- No `ShowInlinePanel`, no `RevealContentFrame`, no reveal gate, no `PlayPanelEnterAnimation`.
- `ShowContent(tag)` becomes: set the header, remember the tag, `ContentFrame.Navigate(pageType)`,
  `TrimContentBackStack()`, optionally move focus.

`MainPage` keeps only what is genuinely shell: `NavigationView`, the custom title bar, the search
box, theme/tint/acrylic application, the accent resources, the nav avatar, back navigation and the
live tile push.

## Moves

| New page | From `MainPage.xaml` | Code-behind to move |
|---|---|---|
| `HomePage` | `LatestNewsPanel` (235–333), incl. the favourites section | `MainPage.Favorites.cs` (158) |
| `BetasPage` | `BetasPanel` (335–339) | none |
| `SocialPage` | `SocialPanel` (341–345) | none |
| `SettingsPage` | `SettingsPanel` (347–~1215) | `MainPage.Settings.cs` (294), `MainPage.Accent.cs` (279), and the settings-UI half of `MainPage.Appearance.cs` (854) |

`GamesPage` and `ProfilePage` already exist and need no change.

### The `MainPage.Appearance.cs` split is the hard part

It currently does two different jobs and only one of them moves:

- **Stays on the shell** — anything that paints: `ApplyTheme`, `ApplyTintColor`,
  `ApplySurfaceBrushes`, `ApplyPaneBrushes`, `PaneAcrylicBrushes`, `RepaintThemeDependentChrome`,
  `IsEffectiveThemeDark`, `_themePaintKey`, `_tintTag`, `_bodyAcrylicOpacity`, `_paneAcrylicOpacity`,
  `_tintScope`. These paint `RootGrid` and the `NavigationView` pane, which live on `MainPage`.
- **Moves to `SettingsPage`** — the controls that drive them: the swatch grids, the sliders, the
  scope radios, the legibility warning, `LoadAppearanceSettings`' control-restoring half,
  `UpdateSwatchChipColors`, `UpdateTintSelection`, `ShowSwatchCheck`, `BaseSwatchName`.

So `SettingsPage` needs a way to ask the shell to repaint. Simplest is a static event or a small
`AppearanceService` that owns the current tint/opacity/scope state and raises a change event the
shell subscribes to. That is a genuine design decision — settle it before moving code, because
`_loadingSettings` (whose initial `true` is load-bearing, see CLAUDE.md) has to end up on whichever
side owns the sliders.

## Gotchas

- **The csproj is old-style with explicit item lists.** Adding a file is always a two-part change:
  the file *and* the `<Compile>` / `<Page>` entry, with `<DependentUpon>` for partials.
- **`x:Uid` values move with their markup and the resw keys do not change.** A mistyped `x:Uid`
  renders blank with no error — check every moved panel on screen.
- **Shared fields in `MainPage.xaml.cs`** (`_loadingSettings`, `_allSearchItems`, `_searchDebounce`,
  the handler-attached flags) are used across partials; work out which side each belongs to.
- **`ShowContent`'s `default:` case is Home.** Keep an explicit tag → page-type map and keep
  `HeaderFor` in step.
- **`AutomationProperties.SetName(ContentHost/ContentScrollViewer, header)`** and the Main landmark
  must land on whatever the new content host is.
- **Back navigation**: `CanGoBackInPlace()` currently reports true only for LoginPage → ProfilePage.
  With every destination in the Frame, `Frame.CanGoBack` becomes meaningful and `TrimContentBackStack`
  probably has to go — decide deliberately rather than by accident.
- **Repo is CRLF.** Use the Edit/Write tools, never `sed -i`; `text=auto` hides the damage in the
  diff. Verify with `file` afterwards.
- **No comments in code.** Every change means adding/updating a bullet in CLAUDE.md's *Per-file
  notes*, not a comment.
- `LangVersion` is 7.3.

## Verification

Command-line MSBuild cannot confirm any of this — it compiles but does not deploy. Build warning-free
with the compile-check from CLAUDE.md, then **F5 in Visual Studio** and walk:
Home → Games → Home → Profile → Home → Games → Settings → Profile.

Success is: no frame of any transition shows the previous destination's content. If anything still
flashes, record a GIF and frame-difference it (`System.Drawing` reads GIF frames; no ffmpeg needed) —
that is how this was diagnosed and it is faster than reasoning about it.
