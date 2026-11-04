# nanoCAD MCP 447

[Documentation](docs/en/index.md)

## Package contents

| Component | Version |
|---|---|
| MCP server | 0.250.0 |
| nanoCAD bridge | 0.207.0 |
| IPC protocol | 0.144 |

The Windows x64 release contains a self-contained MCP server, the nanoCAD bridge and native helpers, the view-capture worker, installation and connection-check scripts, and English documentation. Original project code and documentation use [MIT](LICENSE); [third-party terms](THIRD-PARTY-NOTICES.md) remain separate.

Supported workflows include ordinary 2D drawing, direct and retained parametric modeling of individual parts, editable DWG documentation, local references and loaded point-cloud interpretation. Profile consumers include extrusion, revolution, sweep and loft within their documented requirements. Read [capabilities](docs/en/capabilities.md).

## Changes in 447

- The initial MCP tool list is smaller. The same tools and input rules are available; request the focused schema and operation help for the full details of a selected write.
- The server resolves exact published tool names. Input validation reports malformed geometry and invalid text as request errors.
- The developer workflow includes a Windows managed build and test job, structured test reports, and a way to rerun failed checks by name. See [development](docs/en/development.md).

The nanoCAD bridge and protocol versions are unchanged. The package carries forward the native modeling capabilities and limits documented for [release 446](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/446). The shared validation library includes the request fixes; packaging does not add new native CAD acceptance.

Use the EXE for guided installation or the ready-to-run ZIP for manual setup. The installer is unsigned; installation on a clean computer has not been verified. Read [installation](docs/en/installation.md) for setup, updates and removal.
