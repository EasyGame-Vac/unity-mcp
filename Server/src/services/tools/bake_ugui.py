"""
Bake UGUI prefabs from UIDataNode JSON via the HtmlToUGUI pipeline.

This tool wraps the HtmlToUGUIBakerCore baking logic, exposing it as a standard
MCP tool so AI assistants can generate UGUI prefabs directly in conversation —
no browser, no manual paste, no software switching.

Actions:
  - bake:               Single JSON → prefab
  - bake_batch:         Multiple JSONs → multiple prefabs
  - bake_partial:       Incremental update of a prefab subtree
  - list:               List baked prefabs
  - delete:             Delete a baked prefab
  - get_dsl:            Retrieve the DSL specification text
  - generate_view_script: Auto-generate C# View script with field bindings

Requires the HtmlToUGUI package to be installed in the Unity project.
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
        "Bake UGUI prefabs from UIDataNode JSON, UI-DSL HTML, or lightweight indentation DSL "
        "via the HtmlToUGUI pipeline. Eliminates the need for browser-based conversion. "
        "Actions: bake (single JSON→prefab), bake_batch (multiple JSONs→prefabs), "
        "bake_partial (incremental subtree update), list (list baked prefabs), "
        "delete (remove prefab), get_dsl (retrieve DSL spec for AI), "
        "generate_view_script (auto-generate C# View script with field bindings), "
        "parse_html (HTML→JSON via pure C# parser), "
        "bake_from_html (HTML→JSON→prefab in one step), "
        "parse_dsl (indentation DSL→JSON, ~65% fewer tokens than HTML), "
        "bake_from_dsl (DSL→JSON→prefab, recommended entry point). "
        "Supports use_tmp (TMP vs legacy Text), font_path (custom font asset), "
        "and template_prefab (parent template with Canvas). "
        "Requires HtmlToUGUI package installed in the Unity project."
    ),
    group="core",
    annotations=ToolAnnotations(
        title="Bake UGUI",
        destructiveHint=True,
    ),
)
async def bake_ugui(
    ctx: Context,
    action: Annotated[
        Literal[
            "bake",
            "bake_batch",
            "bake_partial",
            "list",
            "delete",
            "get_dsl",
            "generate_view_script",
            "parse_html",
            "bake_from_html",
            "parse_dsl",
            "bake_from_dsl",
        ],
        "Action to perform.",
    ],
    json_content: Annotated[
        str | None,
        "UIDataNode JSON string (for bake, bake_partial). "
        "Must follow the DSL spec: root node with name, type, x, y, width, height, children, etc.",
    ] = None,
    html_content: Annotated[
        str | None,
        "UI-DSL HTML string (for parse_html, bake_from_html). "
        "Must follow the DSL spec with data-u-type and data-u-name attributes. "
        "Parsed by the pure C# parser — no browser required.",
    ] = None,
    dsl_content: Annotated[
        str | None,
        "Indentation-based UI DSL string (for parse_dsl, bake_from_dsl). "
        "Lightweight format with ~65% fewer tokens than HTML. "
        "Syntax: 'type name props...' with 2-space indentation for nesting. "
        "Example: 'div MyPage 942x2048 bg:#1a1f2e flex:col center\\n  img bg stretch'",
    ] = None,
    prefab_path: Annotated[
        str | None,
        "Output prefab path, Assets-relative (e.g. 'Assets/Baked/LoginPage.prefab'). "
        "Required for bake, bake_partial, delete, generate_view_script.",
    ] = None,
    reference_width: Annotated[
        int,
        "Design reference resolution width. Default 942 (matches DSL spec).",
    ] = 942,
    reference_height: Annotated[
        int,
        "Design reference resolution height. Default 2048 (matches DSL spec).",
    ] = 2048,
    use_tmp: Annotated[
        bool,
        "Use TextMeshPro (TMP) for text components. Set false to use legacy UnityEngine.UI.Text. "
        "Default true. Applies to all bake actions (bake, bake_batch, bake_partial, "
        "bake_from_html, bake_from_dsl).",
    ] = True,
    template_prefab: Annotated[
        str | None,
        "Template prefab path (optional). Root must have a Canvas component. "
        "When provided, baking uses the template as the base structure. "
        "Supported by bake, bake_from_html, and bake_from_dsl actions.",
    ] = None,
    source_html: Annotated[
        str | None,
        "Source HTML asset path (optional, for image path resolution). "
        "Used when JSON references relative image paths.",
    ] = None,
    save_snapshot: Annotated[
        bool,
        "Save a JSON snapshot to Baked/Json/ for version tracking. Default true.",
    ] = True,
    font_path: Annotated[
        str | None,
        "Font asset path, Assets-relative (e.g. 'Assets/Fonts/MyFont.asset' for TMP or "
        "'Assets/Fonts/MyFont.ttf' for legacy Text). When use_tmp=true, loaded as TMP_FontAsset; "
        "when use_tmp=false, loaded as Font. If omitted, falls back to UguiBakeConfig defaults. "
        "Supported by all bake actions.",
    ] = None,
    json_array: Annotated[
        list[dict[str, Any]] | None,
        "Array of items for bake_batch. Each item: { json: '...', prefab_path: '...' } "
        "or a raw UIDataNode object (prefab name derived from root node name).",
    ] = None,
    output_dir: Annotated[
        str | None,
        "Output directory for bake_batch, or search directory for list. "
        "Assets-relative. Default: 'Assets/3rd/HtmlToUGUI/Baked/Prefabs'.",
    ] = None,
    node_path: Annotated[
        str | None,
        "Target node path within the prefab for bake_partial (e.g. 'content/@topHud'). "
        "Slash-separated. The subtree at this path will be replaced.",
    ] = None,
    script_path: Annotated[
        str | None,
        "Output path for the generated C# View script (generate_view_script). "
        "If omitted, derived from prefab path.",
    ] = None,
    namespace: Annotated[
        str,
        "C# namespace for the generated View script. Default 'Game.UI'.",
    ] = "Game.UI",
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action}

    if action == "bake":
        if json_content is None:
            return {"success": False, "message": "Parameter 'json_content' is required for 'bake' action."}
        if prefab_path is None:
            return {"success": False, "message": "Parameter 'prefab_path' is required for 'bake' action."}
        params_dict["json_content"] = json_content
        params_dict["prefab_path"] = prefab_path
        params_dict["reference_width"] = reference_width
        params_dict["reference_height"] = reference_height
        params_dict["use_tmp"] = use_tmp
        params_dict["save_snapshot"] = save_snapshot
        if template_prefab is not None:
            params_dict["template_prefab"] = template_prefab
        if source_html is not None:
            params_dict["source_html"] = source_html
        if font_path is not None:
            params_dict["font_path"] = font_path

    elif action == "bake_batch":
        if json_array is None:
            return {"success": False, "message": "Parameter 'json_array' is required for 'bake_batch' action."}
        params_dict["json_array"] = json_array
        params_dict["output_dir"] = output_dir
        params_dict["reference_width"] = reference_width
        params_dict["reference_height"] = reference_height
        params_dict["use_tmp"] = use_tmp
        if font_path is not None:
            params_dict["font_path"] = font_path

    elif action == "bake_partial":
        if prefab_path is None:
            return {"success": False, "message": "Parameter 'prefab_path' is required for 'bake_partial' action."}
        if node_path is None:
            return {"success": False, "message": "Parameter 'node_path' is required for 'bake_partial' action."}
        if json_content is None:
            return {"success": False, "message": "Parameter 'json_content' is required for 'bake_partial' action."}
        params_dict["prefab_path"] = prefab_path
        params_dict["node_path"] = node_path
        params_dict["json_content"] = json_content
        params_dict["reference_width"] = reference_width
        params_dict["reference_height"] = reference_height
        params_dict["use_tmp"] = use_tmp
        if font_path is not None:
            params_dict["font_path"] = font_path

    elif action == "list":
        if output_dir is not None:
            params_dict["output_dir"] = output_dir

    elif action == "delete":
        if prefab_path is None:
            return {"success": False, "message": "Parameter 'prefab_path' is required for 'delete' action."}
        params_dict["prefab_path"] = prefab_path

    elif action == "get_dsl":
        pass  # No additional params needed

    elif action == "generate_view_script":
        if prefab_path is None:
            return {"success": False, "message": "Parameter 'prefab_path' is required for 'generate_view_script' action."}
        params_dict["prefab_path"] = prefab_path
        params_dict["namespace"] = namespace
        if script_path is not None:
            params_dict["script_path"] = script_path

    elif action == "parse_html":
        if html_content is None:
            return {"success": False, "message": "Parameter 'html_content' is required for 'parse_html' action."}
        params_dict["html_content"] = html_content
        params_dict["reference_width"] = reference_width
        params_dict["reference_height"] = reference_height

    elif action == "bake_from_html":
        if html_content is None:
            return {"success": False, "message": "Parameter 'html_content' is required for 'bake_from_html' action."}
        if prefab_path is None:
            return {"success": False, "message": "Parameter 'prefab_path' is required for 'bake_from_html' action."}
        params_dict["html_content"] = html_content
        params_dict["prefab_path"] = prefab_path
        params_dict["reference_width"] = reference_width
        params_dict["reference_height"] = reference_height
        params_dict["use_tmp"] = use_tmp
        if source_html is not None:
            params_dict["source_html"] = source_html
        if template_prefab is not None:
            params_dict["template_prefab"] = template_prefab
        if font_path is not None:
            params_dict["font_path"] = font_path

    elif action == "parse_dsl":
        if dsl_content is None:
            return {"success": False, "message": "Parameter 'dsl_content' is required for 'parse_dsl' action."}
        params_dict["dsl_content"] = dsl_content
        params_dict["reference_width"] = reference_width
        params_dict["reference_height"] = reference_height

    elif action == "bake_from_dsl":
        if dsl_content is None:
            return {"success": False, "message": "Parameter 'dsl_content' is required for 'bake_from_dsl' action."}
        if prefab_path is None:
            return {"success": False, "message": "Parameter 'prefab_path' is required for 'bake_from_dsl' action."}
        params_dict["dsl_content"] = dsl_content
        params_dict["prefab_path"] = prefab_path
        params_dict["reference_width"] = reference_width
        params_dict["reference_height"] = reference_height
        params_dict["use_tmp"] = use_tmp
        if source_html is not None:
            params_dict["source_html"] = source_html
        if template_prefab is not None:
            params_dict["template_prefab"] = template_prefab
        if font_path is not None:
            params_dict["font_path"] = font_path

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "bake_ugui",
        params_dict,
    )

    if not isinstance(response, dict):
        return {"success": False, "message": str(response)}

    return {
        "success": response.get("success", False),
        "message": response.get("message", response.get("error", "")),
        "data": response.get("data"),
    }
