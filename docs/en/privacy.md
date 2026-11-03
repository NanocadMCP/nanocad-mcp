# Data and privacy

[Documentation](index.md)

## What runs on your computer

The MCP server is a local process started by your AI client. It communicates with a bridge loaded inside nanoCAD through local Windows IPC. The packaged integration does not require an account with a nanoCAD MCP hosting service or a project-specific API key.

The server and bridge must run as the same Windows user, in the same desktop session and at the same elevation level. The local connection is not an internet-accessible CAD service.

## What the assistant can receive

Depending on its tool calls, the assistant can receive object properties, geometry, text, tables, parameters, drawing identifiers and paths, measurements, point-cloud samples and viewport images. A view capture can contain everything visible in that nanoCAD view.

Your AI client may send these tool results, your prompts and supplied files to its provider. Local CAD execution does not mean all AI processing happens locally. Data retention and model-training settings depend on the client/provider and your account or organization policies; this project does not override them.

Before using confidential drawings, confirm that your organization permits the chosen client and provider to process the information. Use a sanitized copy when testing or sharing an example.

## Files and point clouds

CAD edits affect the selected drawing. Saving requires an explicit save operation or normal saving in nanoCAD. Ask the assistant to use your intended output path.

Loaded point-cloud source points and storage remain read-only through this integration. Moving the insertion or creating an editable section profile changes drawing content; it does not rewrite the original cloud.

## Diagnostics and support

Installation and diagnostic files can contain local paths, versions and connection details. Drawing metadata, screenshots and tool results can contain project information. Review attachments before posting them in a public issue. Remove personal information, confidential geometry, credentials and unrelated client settings.

Never post a complete AI-client configuration or access token. See [troubleshooting](troubleshooting.md) for a normal bug report and [security](../../SECURITY.md) for sensitive vulnerability reports.
