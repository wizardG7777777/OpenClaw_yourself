# AGENTS.md - AI Coding Agent Guide

## Project Overview

This is a **Unity 6 (6000.3.11f1)** 3D game project using the **Universal Render Pipeline (URP)**. It's a top-down interior exploration MVP where the player walks around a low-poly room and interacts with objects like doors, curtains, and lights.

**Project Name:** My project  
**Unity Version:** 6000.3.11f1 (LTS)  
**Template:** URP Blank (com.unity.template.urp-blank@17.0.14)  
**Target Platform:** Standalone (macOS primary, with Android build settings configured)  
**Scripting Backend:** IL2CPP (Android), Mono (Editor)  
**API Compatibility:** .NET Standard 2.1  
**Language Version:** C# 9.0

---

## Technology Stack

### Core Packages
- **Render Pipeline:** Universal Render Pipeline (URP) 17.3.0
- **Input System:** Unity Input System 1.19.0 (new Input System, reads `Keyboard.current` / `Mouse.current` directly)
- **Physics:** Unity Physics (PhysX-based 3D physics)
- **Navigation:** AI Navigation 2.0.11
- **Testing:** Unity Test Framework 1.6.0 (NUnit-based)

### Third-Party Packages
- **Tripo3D Unity Bridge:** `com.tripo3d.unitybridge` (local package at `~/Downloads/Tripo3d_Unity_Bridge`)
  - Used for AI-generated 3D model import
- **Unity MCP:** `com.coplaydev.unity-mcp` (from GitHub)
  - Enables editor automation via Model Context Protocol
- **glTF Support:** com.unity.cloud.gltfast 6.16.1

### Development Tools
- **IDE Support:** Visual Studio (2.0.26), Rider (3.0.39)
- **Version Control:** Unity Cloud/Plastic SCM (configured but cloud disabled)
- **Code Editor:** VS Code (settings configured in `.vscode/`)

---

## Project Structure

```
My project/
├── Assets/
│   ├── Scripts/              # Gameplay scripts (see Architecture section)
│   │   ├── IInteractable.cs      # Interface for interactable objects
│   │   ├── PlayerInteraction.cs  # Handles raycast and proximity interaction
│   │   ├── PlayerMovement.cs     # Rigidbody-based WASD movement
│   │   ├── CameraFollow.cs       # Top-down camera tracking
│   │   ├── ToggleDoor.cs         # Door interaction implementation
│   │   ├── ToggleCurtain.cs      # Curtain sliding interaction
│   │   └── ToggleLight.cs        # Light on/off toggle
│   ├── Editor/               # Editor scripts and tests
│   │   └── Tests/
│   │       └── PlayerMovementTests.cs  # NUnit tests for PlayerMovement
│   ├── LowPolyInterior/      # Low-poly interior art pack (prefabs, models, materials)
│   ├── TripoModels/          # AI-generated 3D models from Tripo
│   ├── Screenshots/          # Captured screenshots
│   ├── Scenes/
│   │   └── SampleScene.unity # Default template scene
│   ├── Materials/            # Project materials
│   ├── Settings/             # URP pipeline settings, renderer data
│   ├── TutorialInfo/         # Template documentation files
│   ├── mvp_game.unity        # **Main game scene**
│   └── InputSystem_Actions.inputactions  # Input System actions asset
├── Packages/
│   ├── manifest.json         # Package dependencies
│   └── packages-lock.json    # Locked package versions
├── ProjectSettings/          # Unity project configuration
│   ├── ProjectVersion.txt    # Unity version identifier
│   ├── ProjectSettings.asset # Main project settings
│   ├── EditorBuildSettings.asset # Build scene list
│   ├── TagManager.asset      # Tags and layers
│   └── ... (other .asset config files)
├── .vscode/                  # VS Code configuration
│   ├── settings.json         # Editor settings, file exclusions
│   ├── extensions.json       # Recommended extensions
│   └── launch.json           # Debug configuration
├── ignore.conf               # Plastic SCM ignore rules
└── Library/                  # Unity cache (auto-generated, do not commit)
```

---

## Architecture & Code Organization

### Design Pattern: Interface-Based Interaction System

The project uses a simple but extensible interaction pattern:

```
IInteractable (interface)
    ├── ToggleDoor
    ├── ToggleCurtain
    └── ToggleLight
```

#### Core Components

**1. IInteractable** (`Assets/Scripts/IInteractable.cs`)
```csharp
public interface IInteractable
{
    void Interact();
    string GetPromptText();
}
```
- Defines the contract for all interactable objects
- `Interact()`: Executes the interaction logic
- `GetPromptText()`: Returns UI prompt text (e.g., "Open Door", "Turn Off Light")

**2. PlayerInteraction** (`Assets/Scripts/PlayerInteraction.cs`)
- Attached to the Player GameObject
- Two interaction methods:
  - **Mouse Click:** Raycast from camera to clicked object (max 50 units)
  - **E Key:** Finds nearest `IInteractable` within `interactRange` (default 3f) using `OverlapSphere`
- Configurable: `interactRange`, `interactLayer` (LayerMask)

**3. PlayerMovement** (`Assets/Scripts/PlayerMovement.cs`)
- Rigidbody-based physics movement
- Camera-relative WASD controls (reads `Keyboard.current` directly)
- Features:
  - `moveSpeed`: 3.5f (default)
  - `rotationSpeed`: 10f (for smooth rotation)
  - Legacy `Animation` component integration for walk cycle
- Movement in `FixedUpdate()`, input reading in `Update()`

**4. CameraFollow** (`Assets/Scripts/CameraFollow.cs`)
- Fixed top-down camera with configurable offset
- Default offset: `(0, 6, -5)` - 6 units up, 5 units back
- Fixed rotation: `(55, 0, 0)` - 55-degree angle
- Direct position assignment (no smoothing/Lerp)

#### Interactable Implementations

**ToggleDoor** (`Assets/Scripts/ToggleDoor.cs`)
- **CRITICAL:** Requires a parent pivot object (empty GameObject)
- Script goes on the door mesh child, NOT the pivot
- Rotates the pivot around Y axis by `openAngle` degrees (default 90°)
- Rotation speed: `rotateSpeed` degrees/second (default 180°)
- Uses `Quaternion.RotateTowards` for smooth animation

**ToggleCurtain** (`Assets/Scripts/ToggleCurtain.cs`)
- Slides along local X axis
- `slideOffset`: Distance to slide (default 1.5 units)
- `slideSpeed`: Movement speed (default 3 units/second)
- Uses `Vector3.MoveTowards` for linear interpolation

**ToggleLight** (`Assets/Scripts/ToggleLight.cs`)
- Toggles the `Light` component on/off
- Works with Light on same object or child object (`GetComponentInChildren`)
- Tracks state via `isOn` boolean

---

## Development Workflow

### Using Unity MCP (Model Context Protocol)

This project has `com.coplaydev.unity-mcp` installed for AI-assisted editor automation.

**Key MCP Tools Available:**
- `manage_gameobject` - Create, modify, find GameObjects
- `create_script` / `script_apply_edits` - C# script management
- `manage_scene` - Scene operations, hierarchy queries
- `manage_components` - Add/remove/modify components
- `run_tests` - Execute Unity Test Framework tests
- `read_console` - Check compilation errors and logs
- `manage_camera` - Screenshots, camera positioning
- `manage_asset` - Asset database operations

**Critical Workflow - After Script Changes:**
1. After `create_script` or `script_apply_edits`, the tool auto-triggers compilation
2. Wait for compilation to complete (check `mcpforunity://editor/state`)
3. **Always call `read_console`** to check for compilation errors before proceeding
4. Only then attach components to GameObjects

**Resource-First Approach:**
- Read `mcpforunity://editor/state` before complex operations
- Read `mcpforunity://scene/gameobject-api` to understand the scene
- Use `find_gameobjects` to locate objects before modifying

See the Unity MCP skill at: `/Users/yanchenyu/.claude/skills/unity-mcp-skill/SKILL.md`

### Scene Management

**Main Scene:** `Assets/mvp_game.unity`

To load the main scene via MCP:
```
manage_scene(action="load", scene_path="Assets/mvp_game.unity")
```

**Build Settings Scenes:**
- `Assets/Scenes/SampleScene.unity` (enabled in build)

### Creating New Interactable Objects

When adding a new interactable object:

1. **Create the script:**
   ```csharp
   public class ToggleMyObject : MonoBehaviour, IInteractable
   {
       public void Interact() { /* logic */ }
       public string GetPromptText() { return "Interact"; }
   }
   ```

2. **For doors specifically:**
   - Create empty GameObject as pivot (place at hinge location)
   - Put door mesh as child of pivot
   - Attach `ToggleDoor` to the child mesh object
   - The script rotates the parent pivot

3. **Set up layers:**
   - Place object on a layer included in `PlayerInteraction.interactLayer`
   - Default is `~0` (Everything), but can be restricted

---

## Testing

### Test Framework
- **Framework:** Unity Test Framework 1.6.0 (NUnit)
- **Test Location:** `Assets/Editor/Tests/PlayerMovementTests.cs`
- **Test Mode:** Edit Mode tests (run in Editor, not Play Mode)

### Running Tests

**Via Unity Editor:**
- Window → General → Test Runner
- Select Edit Mode tab
- Click Run All or select specific tests

**Via MCP:**
```
run_tests(mode="EditMode")
```

### Current Test Coverage

`PlayerMovementTests.cs` contains:
- `PlayerMovement_DefaultValues_AreSet` - Verifies fields exist via reflection
- `PlayerMovement_RequiresRigidbody` - Tests Rigidbody requirement
- `PlayerMovement_ExposedFields_ArePublic` - Tests public field accessibility

All tests use proper setup/cleanup with `Object.DestroyImmediate()`.

---

## Build Configuration

### Current Build Settings
- **Active Build Target:** Standalone macOS (Apple Silicon/Intel)
- **Graphics APIs:** Metal (macOS/iOS), OpenGLES3/Vulkan (Android)
- **Color Space:** Linear
- **Scripting Backend:** IL2CPP (Android)

### Supported Platforms (Configured)
- **Standalone:** macOS, Windows, Linux
- **Mobile:** iOS (minimum 15.0), Android (minimum API 25)
- **Console:** Nintendo Switch, PS4, PS5, Xbox One
- **Web:** WebGL (experimental)

### Version Info
- **Bundle Version:** 0.1.0
- **Android Bundle Version Code:** 1

---

## Code Style Guidelines

### Naming Conventions
- **Classes/Methods:** PascalCase (e.g., `PlayerMovement`, `Interact()`)
- **Private fields:** camelCase with underscore prefix (implied by usage)
- **Public fields:** camelCase (e.g., `moveSpeed`, `interactRange`)
- **Constants:** PascalCase or camelCase depending on visibility

### Code Organization
- Use `[Header("Category")]` to group public fields in Inspector
- Use `[Tooltip("Description")]` for all public fields
- Place `Debug.Log()` calls in interaction methods for debugging
- Cache component references in `Awake()` or `Start()`

### Input Handling
- Use the new Input System (UnityEngine.InputSystem)
- Access via `Keyboard.current` and `Mouse.current`
- Check for null before accessing (`if (Keyboard.current != null)`)

### Physics
- Use `Rigidbody` for physics-based movement
- Apply forces/position changes in `FixedUpdate()`
- Read input in `Update()`, apply in `FixedUpdate()`

---

## Asset Pipeline

### Asset Folders
- **`Assets/LowPolyInterior/`** - Low-poly interior art pack
  - Contains: Models, materials, textures, prefabs, demo scenes
  - Demo scenes: `LowPolyInterior_Demo.unity`, `LowPolyInterior_Overview.unity`
- **`Assets/TripoModels/`** - AI-generated 3D models from Tripo3D
- **`Assets/Screenshots/`** - Runtime or editor screenshots
- **`Assets/Materials/`** - Project-specific materials
- **`Assets/Settings/`** - URP pipeline assets and renderer data

### Key Assets
- **Input Actions:** `Assets/InputSystem_Actions.inputactions`
- **Pipeline Asset:** Check `Assets/Settings/` folder
- **Main Scene:** `Assets/mvp_game.unity`

---

## Version Control

### Ignored Files (ignore.conf)
Standard Unity ignore patterns including:
- `Library/`, `Temp/`, `Obj/`, `Build/`, `Builds/`
- `UserSettings/`, `MemoryCaptures/`, `Logs/`
- Generated files: `*.csproj`, `*.sln`, `*.unityproj`
- Platform builds: `*.apk`, `*.aab`, `*.app`
- OS files: `.DS_Store`, `Thumbs.db`

### Tracked Files
- All source code (`Assets/Scripts/`, `Assets/Editor/`)
- Scene files (`.unity`)
- Prefabs (`.prefab`)
- Materials (`.mat`)
- Configuration (`.asset`, `ProjectSettings/`)
- Package manifests (`Packages/manifest.json`)

---

## Common Issues & Solutions

### Door Not Rotating Correctly
- Ensure door has a parent pivot object
- Script must be on the mesh child, not the pivot
- Pivot should be positioned at the hinge location

### Interaction Not Working
- Check that object has a collider
- Verify object is on the correct layer (included in `interactLayer`)
- Ensure object implements `IInteractable`
- Check console for null reference exceptions

### Compilation Errors After Script Changes
- Wait for Unity to finish compiling (check status bar)
- Read console for specific error messages
- Ensure all scripts are saved before compilation

### Tripo3D Package Issues
- Package is installed from local path: `~/Downloads/Tripo3d_Unity_Bridge`
- If missing, the package will show errors in Package Manager
- Project can function without Tripo3D (only needed for model import)

---

## External Dependencies

### Tripo3D Unity Bridge
- **Source:** Local file package
- **Path:** `~/Downloads/Tripo3d_Unity_Bridge`
- **Purpose:** AI-generated 3D model import
- **Note:** This is a local dependency; the project won't compile if the path is invalid

### Unity MCP
- **Source:** GitHub (https://github.com/CoplayDev/unity-mcp)
- **Branch:** main
- **Purpose:** Editor automation via MCP

---

## Useful Resources

### Unity Documentation
- Unity 6 Manual: https://docs.unity3d.com/6000.3/Documentation/Manual/
- Input System: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.9/manual/
- URP: https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/
- Test Framework: https://docs.unity3d.com/Packages/com.unity.test-framework@1.6/manual/

### Project-Specific
- Main scene: `Assets/mvp_game.unity`
- Player prefab: Search for GameObject with `PlayerMovement` component
- Camera: Search for GameObject with `CameraFollow` component

---

*Last updated: 2026-03-21*
