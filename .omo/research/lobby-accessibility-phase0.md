# Phase 0 Preflight: lobby accessibility / say-the-spire2

Date: 2026-06-11
Plan: `.omo/plans/lobby-accessibility-say-the-spire2.md`

## Call-site map

`csharp-ls` is not installed, so LSP references are unavailable in this environment. Static mapping was done with `rg`; build/tests must compensate for missing LSP.

Key call sites:

- `sts2-lan-connect/Scripts/Patches.MultiplayerSubmenu.cs:169` — lobby entry `OnLobbyPressed()`.
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:220` — `ShowOverlay()` calls `RebuildRoomStage()` and `CheckClipboardForInviteCode()`.
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:1463` — shared custom dialog shell.
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:1790` — room list rebuild, frees/recreates cards.
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:2207` — room card creation.
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:2363` — current room card mouse/double-click input.
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:3555` — current clipboard invite detection.
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:3586` — invite confirm dialog open.
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:3695` — invite confirm dialog construction.
- `sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs:438` — chat panel toggle.

## say-the-spire2 bridge text carrier

Source inspected locally at `/var/folders/zn/4py1jdj523j4y6f8y6lcyp_00000gn/T/opencode/say-the-spire2`.

Relevant files:

- `UI/UIManager.cs:35` — `SetFocusedControl(Control control, UIElement? preResolved = null)` only sets current control/element and dirty flag.
- `UI/UIManager.cs:57` — `Update()` resolves and speaks dirty focus.
- `UI/UIManager.cs:130` — if the screen registry does not resolve a control, `ProxyFactory.Create(control)` is used.
- `UI/Elements/ProxyFactory.cs:45` — `LineEdit` resolves to `ProxyTextInput`.
- `UI/Elements/ProxyFactory.cs:130` — unknown focusable Control falls back to generic `ProxyButton`.
- `UI/Elements/ProxyButton.cs:42` — label comes from `OverrideLabel`, child text, sibling label, or meaningful node name.
- `UI/Elements/ProxyElement.cs:59` — child text search reads `Label` and `RichTextLabel`, including `%Label` / `%Title` / all children.
- `UI/Elements/ProxyTextInput.cs:30` — text input label comes from `OverrideLabel`, sibling label, or clean node name; status reads current text or placeholder.

Conclusion:

- Bridge can pass `null` for `UIElement` and rely on say-the-spire2 fallback proxy creation, provided our controls contain a readable `Label`/`RichTextLabel`, meaningful `Name`, or sibling label.
- For room cards (`PanelContainer`), add either a child `Label` containing concise Chinese announcement text or ensure existing first child label is sufficient. Prefer visible/existing labels where possible; use metadata only for our own tests, because say-the-spire2 does not read arbitrary metadata.
- For `LineEdit`, preserve sibling labels and meaningful `Name`/placeholder.
- Do not attempt direct `SpeechManager.Speak/Output`.

## Invite entry skip

`LanConnectInviteCode.Encode(serverBaseUrl, roomId, password)` stores server address in payload `S`, and `TryDecode()` requires non-empty `S` and `R`.

Conclusion: valid invite codes carry enough server information to skip the server picker and then use the existing `AcceptInviteAsync()` server-switch logic.

## Hotkey safety

Repo search found no existing `Key.F7`, `Key.F8`, `Key.T`, `InputMap`, `ui_accept`, `ui_cancel`, `ui_up/down/left/right`, or focus-neighbor usage in `sts2-lan-connect/Scripts`.

Conclusion:

- No conflict with existing mod input code.
- Still avoid `T` by default unless strictly guarded against text-input focus.
- Implement F7/F8 first.

## Implementation constraints

- LSP references are unavailable; use `rg` + build/tests.
- Preserve large-overlay locality: helper classes first, minimal overlay call-site edits.
- Tests must prove pure decision logic before behavior edits.

## GdUnit4 runtime status

- `sts2-lan-connect.GdUnitTests` compiles with `gdUnit4.api 5.1.0-rc1` and `gdUnit4.test.adapter 3.0.0`.
- `/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path sts2-lan-connect.GdUnitTests --quit` works after adding a minimal `project.godot` and `TestMain.tscn`.
- Root cause of the previous `GodotRuntimeExecutor` timeout was missing Godot C# project metadata in `project.godot`: without `[dotnet] project/assembly_name="sts2_lan_connect_gdunit_tests"`, Godot started but logged `.NET: Failed to load project assembly`, so the adapter never connected to the runtime.
- After adding the `[dotnet]` assembly metadata, `dotnet test ... --settings gdunit4.runsettings` runs successfully.

## Final verification snapshot

Fresh verification on 2026-06-12 after the focus-review fixes, the dialog-aware focus traversal patch, and GdUnit runtime fix:

- Pure .NET tests: `dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj` passed (`11` passed, `0` failed, `0` skipped). The Godot source generator emitted the known test-project warning that `GodotProjectDir` is null/empty.
- GdUnit adapter gate: `dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1` passed (`2` passed, `0` failed, `0` skipped). It prints two orphan-node warnings from the minimal smoke scene/test fixture cleanup path.
- Client package/build: `bash scripts/package-sts2-lan-connect.sh` passed with `0` warnings and `0` errors, rebuilt the DLL/PCK, staged artifacts under `sts2-lan-connect/release/.build_mod_output/sts2_lan_connect`, and created `sts2-lan-connect/release/sts2_lan_connect-release.zip`.
- Package SHA-256: `55c196bfc9c5de80a9f23975a2af2c559ef122680e974e5de4f3e0ce6cd1c0c2`.
- Package contents include `sts2_lan_connect.dll`, `sts2_lan_connect.pck`, `sts2_lan_connect.json`, `lobby-defaults.json`, README/user guide, and platform installers: `install-sts2-lan-connect-macos.command`, `install-sts2-lan-connect-macos.sh`, `install-sts2-lan-connect-windows.bat`, `install-sts2-lan-connect-windows.ps1`.
- Packaged and installed `lobby-defaults.json` include `cfDiscoveryBaseUrl: https://sts2-gamelobby-register.xyz` and six seed peers; installed defaults JSON validates with `python3 -m json.tool`.
- Local macOS installer surface passed: `bash scripts/install-sts2-lan-connect-macos.sh --install --package-dir sts2-lan-connect/release/.build_mod_output/sts2_lan_connect --no-save-sync` installed to the STS2 app bundle and refreshed/validated the bundle signature.
- Installed DLL hash matches the freshly staged DLL: `5a27876886f323a27a32a585dcd4249b2b4a0f0f32010a43662f5ac9ff183cac`.
- Installed `lobby-defaults.json` hash matches the freshly staged defaults: `a3bea081cd908a3a0ef6cc56a969074913204679597b1ede00a5efee28e4f3c8`.
- C# LSP diagnostics could not run because `csharp-ls` is not installed in this environment; compiler/test gates were used instead.
- Focus-review re-check passed for the previous HIGH blockers: progress Escape consumption, progress-dialog focus isolation, password-to-resume focus handoff, recursive dialog-button focus search, overlay-level F7/Escape consumption, room-card focus announcements, and dialog-scoped keyboard traversal.
