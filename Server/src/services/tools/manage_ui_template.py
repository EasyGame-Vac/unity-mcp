"""
Defines the manage_ui_template tool for creating uGUI (Canvas-based) UI templates.

Unlike manage_ui (which targets UI Toolkit / UXML), this tool creates
GameObject hierarchies with Canvas, RectTransform, Image, Text and Button
components — the classic uGUI workflow.

Supports preset layouts (e.g. four-corner HUD) and individual element creation.
"""
import asyncio
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import normalize_properties
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.preflight import preflight


@mcp_for_unity_tool(
    group="ugui",
    description=(
        "Creates and manages uGUI (Canvas-based) UI template GameObjects in Unity.\n\n"
        "This tool builds UI hierarchies with Canvas, RectTransform, Image, Text and "
        "Button components — the classic uGUI workflow. For UI Toolkit (UXML/USS) "
        "use manage_ui instead.\n\n"
        "Actions:\n"
        "- create_canvas: Create a root Canvas with CanvasScaler + GraphicRaycaster\n"
        "- create_panel: Create an anchored panel at a screen zone (top_left, top_right, etc.)\n"
        "- create_element: Create a UI element (image, text, button, empty) under a parent\n"
        "- create_from_preset: Create a full layout from a preset (e.g. four_corner_hud)\n"
        "- list_presets: List available preset names and descriptions\n"
        "- get_info: Get hierarchy info of a Canvas or UI element\n\n"
        "Preset 'four_corner_hud' creates:\n"
        "  Canvas (1920x1080 reference)\n"
        "  ├── Zone_TopLeft (character card area)\n"
        "  ├── Zone_TopRight (date/weather area)\n"
        "  ├── Zone_BottomLeft (function icons area)\n"
        "  ├── Zone_BottomRight (lifespan seal area — largest visual weight)\n"
        "  └── Zone_Center (map/game area — no UI, pure gameplay)"
    ),
    annotations=ToolAnnotations(
        title="Manage UI Template",
        destructiveHint=True,
    ),
)
async def manage_ui_template(
    ctx: Context,
    action: Annotated[
        Literal[
            "create_canvas",
            "create_panel",
            "create_element",
            "create_from_preset",
            "list_presets",
            "get_info",
        ],
        "The action to perform.",
    ],
    name: Annotated[
        str,
        "GameObject name for the UI element being created, or Canvas/element path for get_info.",
    ],
    parent: Annotated[
        str | None,
        "Parent GameObject path (e.g. 'Canvas/Zone_TopLeft'). "
        "Required for create_element. Optional for create_panel (defaults to Canvas root)."
    ] = None,
    zone: Annotated[
        str | None,
        "Screen zone for anchoring. One of: 'top_left', 'top_right', 'bottom_left', "
        "'bottom_right', 'center', 'top_full', 'bottom_full', 'full'. "
        "Required for create_panel. Used by create_from_preset internally."
    ] = None,
    element_type: Annotated[
        str | None,
        "Element type for create_element: 'image', 'text', 'button', 'empty'."
    ] = None,
    preset: Annotated[
        str | None,
        "Preset name for create_from_preset: 'four_corner_hud', 'dialog', 'status_bar'."
    ] = None,
    properties: Annotated[
        dict[str, Any] | str | None,
        "Properties dict. Common keys: color (hex string like '#1A1A1A'), "
        "text, fontSize, width, height, anchorMin, anchorMax, pivot, "
        "offsetMin, offsetMax, rotation, scale, spritePath, raycastTarget."
    ] = None,
    canvas_type: Annotated[
        str,
        "Canvas render mode for create_canvas: 'screen_space_overlay', "
        "'screen_space_camera', 'world_space'."
    ] = "screen_space_overlay",
    reference_resolution: Annotated[
        str,
        "Reference resolution for CanvasScaler, e.g. '1920x1080'. "
        "Use match=0 for width-driven, match=1 for height-driven."
    ] = "1920x1080",
    save_as_prefab: Annotated[
        bool,
        "If true, save the created GameObject (and children) as a prefab at prefab_path."
    ] = False,
    prefab_path: Annotated[
        str | None,
        "Asset path to save prefab, e.g. 'Assets/Prefabs/UI/HUD.prefab'. "
        "Required if save_as_prefab is true."
    ] = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()

    # Normalize properties
    properties, parse_error = normalize_properties(properties)
    if parse_error:
        await ctx.error(f"manage_ui_template: {parse_error}")
        return {"success": False, "message": parse_error}

    # List presets is a server-side action — no Unity round-trip needed
    if action == "list_presets":
        return {
            "success": True,
            "message": "Available UI templates.",
            "data": {
                "presets": [
                    {
                        "name": "four_corner_hud",
                        "description": (
                            "Four-corner HUD layout with 5 zones: top_left (character card), "
                            "top_right (date/weather), bottom_left (function icons), "
                            "bottom_right (lifespan seal — largest visual weight), "
                            "center (gameplay area, no UI). "
                            "Based on the UI layout spec: visual weight = information importance."
                        ),
                        "zones": ["top_left", "top_right", "bottom_left", "bottom_right", "center"],
                    },
                    {
                        "name": "dialog",
                        "description": "Centered modal dialog panel with semi-transparent backdrop.",
                        "zones": ["full", "center"],
                    },
                    {
                        "name": "status_bar",
                        "description": "Top or bottom full-width status bar for notifications.",
                        "zones": ["top_full", "bottom_full"],
                    },
                ]
            },
        }

    # Prepare parameters for the C# handler
    params_dict = {
        "action": action.lower(),
        "name": name,
        "parent": parent,
        "zone": zone,
        "elementType": element_type,
        "preset": preset,
        "properties": properties,
        "canvasType": canvas_type,
        "referenceResolution": reference_resolution,
        "saveAsPrefab": save_as_prefab,
        "prefabPath": prefab_path,
    }

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    loop = asyncio.get_running_loop()
    result = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "manage_ui_template", params_dict, loop=loop
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
