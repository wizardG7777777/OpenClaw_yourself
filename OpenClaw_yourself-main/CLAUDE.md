# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Unity 6 (URP) project — a top-down interior game MVP where the player walks around a low-poly room, interacting with doors, curtains, and lights. Uses the new Input System (keyboard WASD + mouse click / E key).

## Key Packages

- **Render pipeline:** Universal Render Pipeline (URP 17.3)
- **Input:** Unity Input System 1.19 (reads `Keyboard.current` / `Mouse.current` directly)
- **3D model import:** Tripo3D Unity Bridge (local package at `~/Downloads/Tripo3d_Unity_Bridge`)
- **MCP integration:** `com.coplaydev.unity-mcp` for editor automation via Claude
- **Navigation:** AI Navigation 2.0
- **glTF:** com.unity.cloud.gltfast 6.16

## Architecture

All gameplay scripts live in `Assets/Scripts/`. The design follows a simple **interface-based interaction pattern**:

- `IInteractable` — interface with `Interact()` and `GetPromptText()`. Any interactable object implements this.
- `PlayerInteraction` — attached to the player; raycasts on mouse click and uses `OverlapSphere` on E key to find the nearest `IInteractable` within range.
- `ToggleDoor`, `ToggleCurtain`, `ToggleLight` — concrete `IInteractable` implementations. Doors rotate via a **parent pivot object** (the script goes on the door mesh child, not the pivot). Curtains slide along local X. Lights toggle their `Light` component.
- `PlayerMovement` — Rigidbody-based, camera-relative WASD movement with legacy `Animation` component for walk cycle.
- `CameraFollow` — fixed top-down camera that tracks the player with a configurable offset and rotation (no smoothing/Lerp, direct follow).

Main scene: `Assets/mvp_game.unity`

## Testing

Tests are in `Assets/Editor/Tests/`. They use NUnit via Unity Test Framework.

Run tests from Unity: **Window > General > Test Runner**, or via the MCP `run_tests` tool.

## Asset Sources

- `Assets/LowPolyInterior/` — low-poly interior art pack (prefabs, models, textures, materials)
- `Assets/TripoModels/` — AI-generated 3D models from Tripo
- `Assets/Screenshots/` — captured screenshots

## Working with This Project via MCP

When using Unity MCP tools:
- After creating/modifying scripts, always call `read_console` to check for compilation errors before proceeding.
- The main scene is `mvp_game`. Use `manage_scene` to query hierarchy.
- New interactable objects should implement `IInteractable` and be placed on a layer included in `PlayerInteraction.interactLayer`.
- Doors require a pivot parent: create an empty GameObject as pivot, put the door mesh as a child, attach `ToggleDoor` to the child.
