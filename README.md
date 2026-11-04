# nanoCAD MCP

[Documentation](docs/en/index.md) · [Installation](docs/en/installation.md) · [Examples](docs/en/examples.md)

Connect an AI assistant to nanoCAD to create and edit drawings, build individual 3D parts, and examine loaded point clouds using ordinary language.

You describe the result, dimensions and constraints. The assistant reads the drawing, works with native nanoCAD objects through Model Context Protocol (MCP), and checks the result with geometry data and images.

## What you can do

- Create and edit 2D geometry, text, dimensions, leaders, tables, hatches and static blocks.
- Build solids with extrusion, revolution, sweep, loft, Boolean operations and finishing tools.
- Use supported native sketches, constraints, parameters and dependent features to build editable individual parts.
- Prepare editable DWG documentation and work with local drawing and raster references.
- Measure and section loaded point clouds, and move or rotate their insertion without changing source points.

Each operation has its own requirements. Read [capabilities and limits](docs/en/capabilities.md) before relying on a workflow. Persistent assembly mates, printing and PDF export are outside this integration.

## Requirements

- 64-bit Windows and nanoCAD 26, with the modules and licenses needed for your operations.
- Codex or Claude Desktop with local MCP support, installed and signed in.
- A Windows account allowed to use nanoCAD and configure the chosen assistant.

The release includes the MCP server runtime. You do not need Python, a compiler or the nanoCAD SDK to use the packaged application. The integration runs locally; your AI client may send drawing information and images to its provider. Read [data and privacy](docs/en/privacy.md).

## Download

Download the installer or ready-to-run ZIP from [release 448](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/448).

| File | Use it for |
|---|---|
| `NanoCAD-MCP-Setup-448.exe` | Guided installation and assistant configuration |
| `nanocad-mcp-local-448.zip` | Manual setup, or installation from an extracted folder |

The release page also provides `SHA256SUMS-448.txt` and archives of the source and documentation from the tagged build.

The downloadable package includes server **0.251.0**, bridge **0.207.0** and protocol **0.144**. Use server and bridge files from the same release.

The installer is unsigned. Installation on a clean computer has not been verified. Follow your organization's installation policy; do not disable security protections.

## Get started

1. Save your drawings and close nanoCAD.
2. Run the installer, check the nanoCAD folder, and select Codex, Claude Desktop, or both.
3. Reopen your assistant, then open nanoCAD and a drawing.
4. Ask: **“Show me the current nanoCAD drawing and report the connection versions without changing the drawing.”**
5. Give a small task with units and dimensions. Ask the assistant to check the result, then save the DWG to your chosen path.

For manual ZIP setup, updates and removal, read [installation](docs/en/installation.md). If the first check fails, use [troubleshooting](docs/en/troubleshooting.md).

Example first task:

> In a new drawing, use millimetres. Draw a 100 × 60 mm rectangular plate outline with four Ø8 mm holes, each centre 10 mm from its two nearest edges. Add readable overall and hole dimensions. Check the geometry and show the result. Save it as `plate.dwg` in the folder I specify.

## Documentation

| I want to… | Read |
|---|---|
| Install or connect manually | [Installation](docs/en/installation.md) |
| Learn how to give useful tasks | [User guide](docs/en/user-guide.md) |
| Find supported features and current limitations | [Capabilities](docs/en/capabilities.md) |
| Try example tasks | [Examples](docs/en/examples.md) |
| Resolve a connection or operation problem | [Troubleshooting](docs/en/troubleshooting.md) |
| Understand what data is processed | [Data and privacy](docs/en/privacy.md) |
| Build or change the project | [Development](docs/en/development.md) |
| Report a bug or contribute | [Contributing](CONTRIBUTING.md) |

## License

Original nanoCAD MCP code and documentation are licensed under the [MIT License](LICENSE).

Copyright (c) 2026 Andrey Juliovich Camejo Severinov.

Commercial use, modification and redistribution are permitted under MIT. This license imposes no restrictions on drawings, models or other results created using nanoCAD MCP. Third-party components, nanoCAD, its SDK and third-party sample files retain their own licenses; see [third-party notices](THIRD-PARTY-NOTICES.md).

This is an independent community project. The project license does not grant a nanoCAD license or an AI service subscription. The software is provided without warranty under MIT.
