# The desktop pedalboard

[Documentation index](README.md)

SharpAmp's pedalboard turns the connected path from **INPUT** to **OUTPUT** into one serial `ProcessorChain`. Effects can also sit unconnected on the board. You can change the wiring during playback and arrange the workspace independently of the sound path.

## Start playing

1. Run `dotnet run --project SharpAmp.Avalonia` on Windows with the .NET 10 SDK installed.
2. Open **Settings** and choose your input and output audio drivers and devices. The advanced section contains sample rate, channels, latency, buffering, and ASIO channel offsets.
3. Click **Save settings** to remember your choices on this computer.
4. Return to **Pedalboard** and click **Start audio**. The initial board is **hard overdrive → reverb**. Use headphones and start at a low output level when routing a microphone.
5. Select an effect block to see its controls in **Effect settings**. Move a slider or enter an exact value to change its sound.

Stop audio before changing devices or audio timing. Effect editing and workspace preferences stay available during playback. If a selected device is unavailable, SharpAmp shows the problem without choosing another device for you. Reconnect it, click **Refresh**, and select it again as needed.

## Build and arrange a sound

Drag an effect from **Add an effect** onto the board, or click its **+** button. New effects are **unconnected** and do not change the sound until wired into the path. While dragging from the library, a preview shows where the block will land; release inside the board to add it, or press **Escape** or release outside the board to cancel. New blocks honor grid snapping. The search box matches names and categories. On narrower windows, choose an effect from the picker above the board and click **+ Add effect**. A board can contain up to 64 effects, including repeated instances of the same type.

Drag from an output point to an input point and release to connect them. You can also drag in the opposite direction, or click the two points in sequence. The **INPUT** and **OUTPUT** points at the top of the board work the same way. Each port accepts one cable: connecting an occupied port replaces only that port's previous cable, leaving all other cables and card positions alone. Self-connections and feedback loops are rejected. Routing is serial; parallel branches and mixers are not supported.

To insert a new effect between two existing effects, connect the upstream effect's output to the new effect's input, then the new effect's output to the downstream effect's input. To move an existing cable, grab it near an endpoint and drag it to another port. A dashed preview shows the pending connection; the sound changes only after a valid drop. **Escape**, losing pointer capture, or releasing on empty space cancels. Right-click a cable or port and choose **Disconnect** to leave it hanging.

Only a complete path from **INPUT** to **OUTPUT** is heard. An incomplete path produces silence and displays **No output path**; **Bypass all** still plays the clean input. Disconnected effects keep all their settings. Blocks on the complete path show processing numbers; other blocks show their connection status. **Earlier / Later** and **Alt+Left / Alt+Right** swap effects within the complete path without moving the cards.

Drag a block's header to move it on the canvas. This changes its position only; it does not change the sound order. **Arrange** places blocks neatly in processing order. **Settings → Snap blocks to the grid** controls snapping while dragging. Scroll the board to reach effects outside the visible area.

Use **Duplicate** to add an unconnected copy of the selected effect, or **Remove** to delete it and its attached cables. Removal does not automatically join its neighbors. **New** starts an empty board with a direct **INPUT → OUTPUT** cable for clean sound. An individual **Bypass effect** switch skips that effect while retaining its parameter values and connections. **Bypass all** lets you compare the complete sound with the clean input.

Changes to the audible path, its parameters, or individual bypass states are coalesced over a 30 ms interval, then create a fresh chain and crossfade to it over 10 ms. This resets the chain's delay/reverb tails and modulation history. The global **Bypass all** switch uses a 10 ms transition while the current effects continue running, so their history is preserved unless you also edit the sound. Moving, selecting, arranging, or editing disconnected blocks does not rebuild the audible chain. Stopping audio cuts playback and its tails immediately.

## Presets, layouts, and settings

These are three separate kinds of saved data:

| Data | How to save/load | What it contains |
| --- | --- | --- |
| Sound preset | **Save preset / Open preset**, suggested `.sharpamp.json` | Board name, every effect's ID, parameters and bypass state, plus explicit cables, including disconnected sections. |
| Board layout | **Layout → Save layout / Load layout**, suggested `.layout.json` | Block IDs/types and canvas coordinates. Loading does not change the sound or its order. |
| App settings | **Settings → Save settings**; restored at startup | Audio devices, drivers, sample rate, channels, timing, ASIO offsets, helpful tips, and grid snapping. |

Save a desktop preset and its layout together. To restore both, open the preset first and then load its layout. Layouts match saved effect IDs; a separately imported copy can also match by the same effect count and ordered types. Incompatible layouts are rejected without changing the board.

**Unsaved changes** refers to the sound preset. Starting a new board, opening another preset, or closing with unsaved sound changes offers **Save preset**, **Discard changes**, and **Cancel**. Block movement is saved only through **Save layout** and does not trigger that prompt. Settings also require their own **Save settings** action. There is no automatic preset, layout, or settings save on exit. The global **Bypass all** switch is a listening control and is not stored in a preset.

App settings are stored in `%LocalAppData%\SharpAmp\settings.json`. Saving writes a complete temporary file before replacing the previous one. Unreadable, invalid, or unsupported settings documents are kept unchanged and reported in Settings. Correct or move that file before saving again. Saved device IDs are matched against current enumeration; missing devices remain unselected.

### Desktop and CLI preset compatibility

**Open preset** accepts the existing [CLI/Core JSON format](Presets.md), including presets in the repository's `presets` directory, and version 1 desktop presets. Those formats are migrated into a serial path in their saved order. Version 2 desktop saves preserve explicit connections:

```json
{
  "version": 2,
  "name": "Clean boost",
  "processors": [
    {
      "id": "b7c3c7288d3b40c5a08e3d3ccba64dd0",
      "type": "gain",
      "isBypassed": false,
      "parameters": { "gain": 1.25 }
    }
  ],
  "connections": [
    { "sourceId": "$input", "targetId": "b7c3c7288d3b40c5a08e3d3ccba64dd0" },
    { "sourceId": "b7c3c7288d3b40c5a08e3d3ccba64dd0", "targetId": "$output" }
  ]
}
```

The CLI/Core loader rejects these additional fields, so changing the filename does not convert the format. For CLI use, follow the complete **INPUT → OUTPUT** path and create a separate document containing only its `processors` array in that order, with each effect's `type` and `parameters`; omit bypassed and disconnected effects. There is no desktop CLI-export command. Preset and layout files are validated before replacing the current board; a failed load reports its error and keeps the current sound. Version 2 presets require a connections array, which may be empty for a silent, disconnected board.

## Keyboard and accessibility

Use **Tab / Shift+Tab** to move between controls. Buttons, connection points, bypass switches, effect pickers, and numeric inputs are keyboard reachable. Focusing a block or one of its controls selects that effect. Sliders and exact numeric inputs carry parameter names; connection points identify input and output.

| Action | Keyboard workflow |
| --- | --- |
| Add an effect | Tab to the effect's **+** button and activate it; in the compact layout choose an effect in the picker, then activate **+ Add effect**. |
| Adjust a setting | Tab to its slider and use arrow keys, or tab to the exact numeric input and type a value. |
| Bypass one effect | With focus on the board/card, press **B**, or focus and toggle **Bypass effect** in the inspector. |
| Remove an effect | With focus on the board/card, press **Delete**, or activate **Remove** in the inspector. |
| Change processing order | With focus on the board/card, use **Alt+Left / Alt+Right**, or activate **Earlier / Later**. |
| Connect two ports | Activate an output button and then an input button, or the reverse. INPUT and OUTPUT terminals are also keyboard reachable. |
| Disconnect | Open a port's context menu and choose its disconnect action. |
| Cancel a pending connection | Press **Escape** while focus is on the board. |
| Arrange positions without dragging | Activate **Arrange** to lay out the whole board. |
| Save or load | Tab to the named preset buttons, the **Layout** menu, or **Save settings**. |

The board shortcuts apply while focus is inside the board; they do not replace text editing keys in parameter or preset-name inputs. Arbitrary block positioning currently uses dragging; **Arrange** provides a keyboard alternative for keeping the board readable. Bypass is identified by text as well as the faded block and dashed connection, and processing order has numbers as well as arrows.

## Manual verification

Automated tests cover document validation, settings persistence, explicit routing, DSP changes, and audio-session lifecycle without opening hardware. The following checks require desktop interaction or an actual audio device:

- At a wide window size and the minimum supported size, add and select effects, reach every inspector control, scroll the board, and confirm the compact effect picker appears when needed.
- Complete a sound-editing session with only the keyboard: add an effect, change a numeric value, reorder it, bypass it, duplicate/remove it, and save a preset. Check visible focus and announced control names with the screen reader you use.
- Drag blocks, toggle snapping, use **Arrange**, save a layout, move the blocks, and reload it. Confirm that positions return without changing effect values or order.
- Save a preset containing duplicate effect types and a bypassed effect. Reopen it and its layout; verify name, order, values, bypass state, and positions. Try a CLI preset and an invalid JSON file; confirm a failed load leaves the current board intact.
- Change audio and workspace settings, save, restart, and verify restoration. Disconnect a saved device and confirm that no replacement is selected automatically.
- With a real input/output route at a low listening level, edit parameters and add, reorder, remove, and bypass effects during playback. Check transitions, responsiveness, and the documented reset of effect history. Compare global bypass and confirm the current effects continue advancing while bypassed.
- Stop/start audio, close with unsaved preset changes, cancel and complete the save prompt, and disconnect a device during playback. Confirm errors are visible and closing releases the hardware.

These checks are a verification guide, not a claim that every driver, screen reader, or hardware combination has been tested. Audible quality and latency must be evaluated on the intended devices.
