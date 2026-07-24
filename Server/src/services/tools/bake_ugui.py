"""
Bake UGUI prefabs from UI-DSL HTML via the HtmlToUGUI pipeline.

This tool wraps the HtmlToUGUIBakerCore baking logic, exposing it as a standard
MCP tool so AI assistants can generate UGUI prefabs directly in conversation —
no browser, no manual paste, no software switching.

The AI assistant is responsible for converting arbitrary input (natural language,
screenshots, existing HTML) into standard UI-DSL HTML before calling this tool.
The tool's sole job is HTML → prefab conversion.

Actions:
  - bake_from_html:       HTML → JSON → prefab (the only baking entry point)
  - list:                 List baked prefabs
  - delete:               Delete a baked prefab
  - get_spec:             Retrieve the UI-DSL HTML specification for AI reference
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
        "Bake UGUI prefabs from UI-DSL HTML via the HtmlToUGUI pipeline. "
        "The AI assistant converts arbitrary input (natural language, screenshots, "
        "existing HTML) into standard UI-DSL HTML, then calls this tool to bake. "
        "The baked HTML is returned in the result as 'htmlContent'. "
        "Actions: bake_from_html (HTML→prefab, sole baking entry point), "
        "list (list baked prefabs), delete (remove prefab), "
        "get_spec (retrieve UI-DSL HTML spec for AI reference), "
        "generate_view_script (auto-generate C# View script with field bindings). "
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
            "bake_from_html",
            "list",
            "delete",
            "get_spec",
            "generate_view_script",
        ],
        "Action to perform.",
    ],
    html_content: Annotated[
        str | None,
        "UI-DSL HTML string (for bake_from_html). "
        "Must follow the DSL spec with data-u-type and data-u-name attributes. "
        "Parsed by the pure C# parser — no browser required. "
        "The AI assistant is responsible for generating this HTML from arbitrary "
        "input (natural language, screenshots, existing HTML, etc.).",
    ] = None,
    prefab_path: Annotated[
        str | None,
        "Output prefab path, Assets-relative (e.g. 'Assets/Baked/LoginPage.prefab'). "
        "Required for bake_from_html, delete, generate_view_script.",
    ] = None,
    reference_width: Annotated[
        int,
        "Design reference resolution width. Default 942.",
    ] = 942,
    reference_height: Annotated[
        int,
        "Design reference resolution height. Default 2048.",
    ] = 2048,
    use_tmp: Annotated[
        bool,
        "Use TextMeshPro (TMP) for text components. Set false to use legacy "
        "UnityEngine.UI.Text. Default true.",
    ] = True,
    template_prefab: Annotated[
        str | None,
        "Template prefab path (optional). Root must have a Canvas component. "
        "When provided, baking uses the template as the base structure.",
    ] = None,
    source_html: Annotated[
        str | None,
        "Source HTML asset path (optional, for image path resolution). "
        "Used when HTML references relative image paths.",
    ] = None,
    font_path: Annotated[
        str | None,
        "Font asset path, Assets-relative (e.g. 'Assets/Fonts/MyFont.asset' for TMP or "
        "'Assets/Fonts/MyFont.ttf' for legacy Text). When use_tmp=true, loaded as TMP_FontAsset; "
        "when use_tmp=false, loaded as Font. If omitted, falls back to UguiBakeConfig defaults.",
    ] = None,
    user_input_content: Annotated[
        str | None,
        "Original user input content to save beside the generated prefab. "
        "Use this for natural-language prompts, pasted HTML, or text-based specs.",
    ] = None,
    user_input_extension: Annotated[
        str,
        "File extension for saved user input content. Default 'txt'. "
        "Examples: 'txt', 'html', 'md'.",
    ] = "txt",
    user_input_source_path: Annotated[
        str | None,
        "Original user input file path to copy beside the generated prefab when the input is a file, "
        "for example a screenshot or source HTML file.",
    ] = None,
    output_dir: Annotated[
        str | None,
        "Search directory for list action. "
        "Assets-relative. Default: 'Assets/MCP/UguiBake/Baked/Prefabs'.",
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

    if action == "bake_from_html":
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
        if user_input_content is not None:
            params_dict["user_input_content"] = user_input_content
            params_dict["user_input_extension"] = user_input_extension
        if user_input_source_path is not None:
            params_dict["user_input_source_path"] = user_input_source_path

    elif action == "list":
        if output_dir is not None:
            params_dict["output_dir"] = output_dir

    elif action == "delete":
        if prefab_path is None:
            return {"success": False, "message": "Parameter 'prefab_path' is required for 'delete' action."}
        params_dict["prefab_path"] = prefab_path

    elif action == "get_spec":
        pass  # No additional params needed

    elif action == "generate_view_script":
        if prefab_path is None:
            return {"success": False, "message": "Parameter 'prefab_path' is required for 'generate_view_script' action."}
        params_dict["prefab_path"] = prefab_path
        params_dict["namespace"] = namespace
        if script_path is not None:
            params_dict["script_path"] = script_path

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
