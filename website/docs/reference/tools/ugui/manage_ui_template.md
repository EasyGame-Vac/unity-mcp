---
title: manage_ui_template
sidebar_label: manage_ui_template
description: "Creates and manages uGUI (Canvas-based) UI template GameObjects in Unity."
---

# `manage_ui_template`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `ugui` &nbsp;·&nbsp; **Module:** `services.tools.manage_ui_template`

## Description

Creates and manages uGUI (Canvas-based) UI template GameObjects in Unity.

This tool builds UI hierarchies with Canvas, RectTransform, Image, Text and Button components — the classic uGUI workflow. For UI Toolkit (UXML/USS) use manage_ui instead.

Actions:
- create_canvas: Create a root Canvas with CanvasScaler + GraphicRaycaster
- create_panel: Create an anchored panel at a screen zone (top_left, top_right, etc.)
- create_element: Create a UI element (image, text, button, empty) under a parent
- create_from_preset: Create a full layout from a preset (e.g. four_corner_hud)
- list_presets: List available preset names and descriptions
- get_info: Get hierarchy info of a Canvas or UI element

Preset 'four_corner_hud' creates:
  Canvas (1920x1080 reference)
  ├── Zone_TopLeft (character card area)
  ├── Zone_TopRight (date/weather area)
  ├── Zone_BottomLeft (function icons area)
  ├── Zone_BottomRight (lifespan seal area — largest visual weight)
  └── Zone_Center (map/game area — no UI, pure gameplay)

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['create_canvas', 'create_panel', 'create_element', 'create_from_preset', 'list_presets', 'get_info']` | yes | The action to perform. |
| `name` | `str` | yes | GameObject name for the UI element being created, or Canvas/element path for get_info. |
| `parent` | `str \| None` | — | Parent GameObject path (e.g. 'Canvas/Zone_TopLeft'). Required for create_element. Optional for create_panel (defaults to Canvas root). |
| `zone` | `str \| None` | — | Screen zone for anchoring. One of: 'top_left', 'top_right', 'bottom_left', 'bottom_right', 'center', 'top_full', 'bottom_full', 'full'. Required for create_panel. Used by create_from_preset internally. |
| `element_type` | `str \| None` | — | Element type for create_element: 'image', 'text', 'button', 'empty'. |
| `preset` | `str \| None` | — | Preset name for create_from_preset: 'four_corner_hud', 'dialog', 'status_bar'. |
| `properties` | `dict[str, Any] \| str \| None` | — | Properties dict. Common keys: color (hex string like '#1A1A1A'), text, fontSize, width, height, anchorMin, anchorMax, pivot, offsetMin, offsetMax, rotation, scale, spritePath, raycastTarget. |
| `canvas_type` | `str` | — | Canvas render mode for create_canvas: 'screen_space_overlay', 'screen_space_camera', 'world_space'. |
| `reference_resolution` | `str` | — | Reference resolution for CanvasScaler, e.g. '1920x1080'. Use match=0 for width-driven, match=1 for height-driven. |
| `save_as_prefab` | `bool` | — | If true, save the created GameObject (and children) as a prefab at prefab_path. |
| `prefab_path` | `str \| None` | — | Asset path to save prefab, e.g. 'Assets/Prefabs/UI/HUD.prefab'. Required if save_as_prefab is true. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

