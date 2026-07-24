"""
Bake particle VFX prefabs from VFX-DSL JSON via the built-in VfxBake pipeline.

This tool wraps the VfxPrefabBakerCore baking logic, exposing it as a standard
MCP tool so AI assistants can generate ParticleSystem prefabs directly in
conversation — no manual Inspector tweaking, no software switching.

The AI assistant is responsible for converting arbitrary input (natural
language, reference descriptions, existing particle setups) into standard
VFX-DSL JSON before calling this tool. The tool's sole job is JSON → prefab
conversion.

Actions:
  - bake_from_json: JSON DSL → prefab (the only baking entry point)
  - list:           List baked prefabs
  - delete:         Delete a baked prefab
  - get_spec:       Retrieve the VFX-DSL JSON specification for AI reference
  - list_textures:  List available effect textures in a directory (for renderer.texture)

The VfxBake pipeline is built into the MCPForUnity package — no external
package dependency required.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description=(
        "Bake particle VFX prefabs from VFX-DSL JSON via the built-in VfxBake pipeline. "
        "The AI assistant converts arbitrary input (natural language, reference "
        "descriptions, etc.) into standard VFX-DSL JSON, then calls this tool to bake. "
        "The JSON describes a tree of ParticleSystems: {name, systems:[{name, transform, "
        "main, emission, shape, colorOverLifetime, sizeOverLifetime, velocityOverLifetime, "
        "textureSheetAnimation, trails, noise, renderer, children}]}. Numbers accept a scalar or [min,max] for random-between-"
        "two-constants; colors accept #rgb/#rgba/#rrggbb/#rrggbbaa hex; curves accept "
        "[[t,v],...] keyframe arrays. "
        "Actions: bake_from_json (JSON→prefab, sole baking entry point), "
        "list (list baked prefabs), delete (remove prefab), "
        "get_spec (retrieve the full VFX-DSL JSON spec — call this before authoring JSON), "
        "list_textures (list available effect textures in a directory, for renderer.texture). "
        "The baked prefab root carries a VfxAutoDestroy component that destroys the "
        "instance after non-looping systems finish playing. "
        "The VfxBake pipeline is built into the MCPForUnity package."
    ),
    group="core",
    annotations=ToolAnnotations(
        title="Bake VFX",
        destructiveHint=True,
    ),
)
async def bake_vfx(
    ctx: Context,
    action: Annotated[
        Literal[
            "bake_from_json",
            "list",
            "delete",
            "get_spec",
            "list_textures",
        ],
        "Action to perform.",
    ],
    json_content: Annotated[
        str | None,
        "VFX-DSL JSON string (for bake_from_json). "
        "Must follow the DSL spec (call get_spec for the full reference). "
        "Top-level: {name, systems:[...]}. Each system maps to one GameObject "
        "with a ParticleSystem; 'children' recurses. "
        "The AI assistant is responsible for generating this JSON from arbitrary "
        "input (natural language, reference descriptions, etc.).",
    ] = None,
    json_path: Annotated[
        str | None,
        "Path to a VFX-DSL JSON file (for bake_from_json). Alternative to json_content: "
        "the Unity side reads the file content directly. Assets-relative or absolute path. "
        "Enables an iterate-by-editing-file workflow: create the JSON once, then only edit "
        "the file and re-bake.",
    ] = None,
    prefab_path: Annotated[
        str | None,
        "Output prefab path, Assets-relative "
        "(e.g. 'Assets/GameTest/Battle2D/Vfx/Explosion_Fire.prefab'). "
        "Required for bake_from_json and delete.",
    ] = None,
    source_json: Annotated[
        str | None,
        "Source JSON asset path (optional, for traceability). "
        "Recorded in the bake result so callers can trace the prefab back to its source.",
    ] = None,
    output_dir: Annotated[
        str | None,
        "Search directory for list action. "
        "Assets-relative. Default: 'Assets/MCP/VfxBake/Baked/Prefabs'.",
    ] = None,
    search_dir: Annotated[
        str | None,
        "Search directory for list_textures action. "
        "Assets-relative. Default: 'Assets/GameEffect/Texture'. "
        "Recursively lists Texture2D assets (path/name/width/height) so the AI "
        "can pick textures to reference via renderer.texture in the VFX-DSL JSON.",
    ] = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action}

    if action == "bake_from_json":
        if json_content is None and json_path is None:
            return {"success": False, "message": "Parameter 'json_content' or 'json_path' is required for 'bake_from_json' action."}
        if prefab_path is None:
            return {"success": False, "message": "Parameter 'prefab_path' is required for 'bake_from_json' action."}
        if json_content is not None:
            params_dict["json_content"] = json_content
        if json_path is not None:
            params_dict["json_path"] = json_path
        params_dict["prefab_path"] = prefab_path
        if source_json is not None:
            params_dict["source_json"] = source_json

    elif action == "list":
        if output_dir is not None:
            params_dict["output_dir"] = output_dir

    elif action == "delete":
        if prefab_path is None:
            return {"success": False, "message": "Parameter 'prefab_path' is required for 'delete' action."}
        params_dict["prefab_path"] = prefab_path

    elif action == "get_spec":
        pass  # No additional params needed

    elif action == "list_textures":
        if search_dir is not None:
            params_dict["search_dir"] = search_dir

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "bake_vfx",
        params_dict,
    )

    if not isinstance(response, dict):
        return {"success": False, "message": str(response)}

    return {
        "success": response.get("success", False),
        "message": response.get("message", response.get("error", "")),
        "data": response.get("data"),
    }
