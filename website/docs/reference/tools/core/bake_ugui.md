---
title: bake_ugui
sidebar_label: bake_ugui
description: "Bake UGUI prefabs from UI-DSL HTML via the built-in UguiBake pipeline."
---

# `bake_ugui`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.bake_ugui`

## Description

Bake UGUI prefabs from UI-DSL HTML via the built-in UguiBake pipeline. The AI assistant converts arbitrary input (natural language, screenshots, existing HTML) into standard UI-DSL HTML, then calls this tool to bake. The baked HTML is returned in the result as 'htmlContent'. Actions: bake_from_html (HTML→prefab, sole baking entry point), list (list baked prefabs), delete (remove prefab), get_spec (retrieve UI-DSL HTML spec for AI reference), generate_view_script (auto-generate C# View script with field bindings). Supports use_tmp (TMP vs legacy Text), font_path (custom font asset), template_prefab (parent template with Canvas), and attach_script (attach a MonoBehaviour to the baked prefab root). The UguiBake pipeline is built into the MCPForUnity package.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['bake_from_html', 'list', 'delete', 'get_spec', 'generate_view_script']` | yes | Action to perform. |
| `html_content` | `str \| None` | — | UI-DSL HTML string (for bake_from_html). Must follow the DSL spec with data-u-type and data-u-name attributes. Parsed by the pure C# parser — no browser required. The AI assistant is responsible for generating this HTML from arbitrary input (natural language, screenshots, existing HTML, etc.). |
| `html_path` | `str \| None` | — | Path to a UI-DSL HTML file (for bake_from_html). Alternative to html_content: the Unity side reads the file content directly. Assets-relative or absolute path. Enables an iterate-by-editing-file workflow: create the HTML once, then only edit the file and re-bake. |
| `prefab_path` | `str \| None` | — | Output prefab path, Assets-relative (e.g. 'Assets/Baked/LoginPage.prefab'). Required for bake_from_html, delete, generate_view_script. |
| `reference_width` | `int` | — | Design reference resolution width. Default 942. |
| `reference_height` | `int` | — | Design reference resolution height. Default 2048. |
| `use_tmp` | `bool` | — | Use TextMeshPro (TMP) for text components. Set false to use legacy UnityEngine.UI.Text. Default true. |
| `template_prefab` | `str \| None` | — | Template prefab path (optional). Root must have a Canvas component. When provided, baking uses the template as the base structure. |
| `source_html` | `str \| None` | — | Source HTML asset path (optional, for image path resolution). Used when HTML references relative image paths. |
| `font_path` | `str \| None` | — | Font asset path, Assets-relative (e.g. 'Assets/Fonts/MyFont.asset' for TMP or 'Assets/Fonts/MyFont.ttf' for legacy Text). When use_tmp=true, loaded as TMP_FontAsset; when use_tmp=false, loaded as Font. If omitted, falls back to UguiBakeConfig defaults. |
| `user_input_content` | `str \| None` | — | Original user input content to save beside the generated prefab. Use this for natural-language prompts, pasted HTML, or text-based specs. |
| `user_input_extension` | `str` | — | File extension for saved user input content. Default 'txt'. Examples: 'txt', 'html', 'md'. |
| `user_input_source_path` | `str \| None` | — | Original user input file path to copy beside the generated prefab when the input is a file, for example a screenshot or source HTML file. |
| `output_dir` | `str \| None` | — | Search directory for list action. Assets-relative. Default: 'Assets/MCP/UguiBake/Baked/Prefabs'. |
| `script_path` | `str \| None` | — | Output path for the generated C# View script (generate_view_script). If omitted, derived from prefab path. |
| `namespace` | `str` | — | C# namespace for the generated View script. Default 'Game.UI'. |
| `attach_script` | `str \| None` | — | MonoBehaviour type name to attach to the baked prefab root (for bake_from_html). Accepts short name (e.g. 'SkillEditorPanel') or fully-qualified name (e.g. 'Game.Battle2D.SkillEditorPanel'). The type must already be compiled in the project. When provided, the script component is added to the prefab root after baking. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

