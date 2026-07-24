---
title: bake_vfx
sidebar_label: bake_vfx
description: "Bake particle VFX prefabs from VFX-DSL JSON via the built-in VfxBake pipeline."
---

# `bake_vfx`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.bake_vfx`

## Description

Bake particle VFX prefabs from VFX-DSL JSON via the built-in VfxBake pipeline. The AI assistant converts arbitrary input (natural language, reference descriptions, etc.) into standard VFX-DSL JSON, then calls this tool to bake. The JSON describes a tree of ParticleSystems: {name, systems:[{name, transform, main, emission, shape, colorOverLifetime, sizeOverLifetime, velocityOverLifetime, textureSheetAnimation, trails, noise, renderer, children}]}. Numbers accept a scalar or [min,max] for random-between-two-constants; colors accept #rgb/#rgba/#rrggbb/#rrggbbaa hex; curves accept [[t,v],...] keyframe arrays. Actions: bake_from_json (JSON→prefab, sole baking entry point), list (list baked prefabs), delete (remove prefab), get_spec (retrieve the full VFX-DSL JSON spec — call this before authoring JSON), list_textures (list available effect textures in a directory, for renderer.texture). The baked prefab root carries a VfxAutoDestroy component that destroys the instance after non-looping systems finish playing. The VfxBake pipeline is built into the MCPForUnity package.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['bake_from_json', 'list', 'delete', 'get_spec', 'list_textures']` | yes | Action to perform. |
| `json_content` | `str \| None` | — | VFX-DSL JSON string (for bake_from_json). Must follow the DSL spec (call get_spec for the full reference). Top-level: {name, systems:[...]}. Each system maps to one GameObject with a ParticleSystem; 'children' recurses. The AI assistant is responsible for generating this JSON from arbitrary input (natural language, reference descriptions, etc.). |
| `json_path` | `str \| None` | — | Path to a VFX-DSL JSON file (for bake_from_json). Alternative to json_content: the Unity side reads the file content directly. Assets-relative or absolute path. Enables an iterate-by-editing-file workflow: create the JSON once, then only edit the file and re-bake. |
| `prefab_path` | `str \| None` | — | Output prefab path, Assets-relative (e.g. 'Assets/GameTest/Battle2D/Vfx/Explosion_Fire.prefab'). Required for bake_from_json and delete. |
| `source_json` | `str \| None` | — | Source JSON asset path (optional, for traceability). Recorded in the bake result so callers can trace the prefab back to its source. |
| `output_dir` | `str \| None` | — | Search directory for list action. Assets-relative. Default: 'Assets/MCP/VfxBake/Baked/Prefabs'. |
| `search_dir` | `str \| None` | — | Search directory for list_textures action. Assets-relative. Default: 'Assets/GameEffect/Texture'. Recursively lists Texture2D assets (path/name/width/height) so the AI can pick textures to reference via renderer.texture in the VFX-DSL JSON. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

