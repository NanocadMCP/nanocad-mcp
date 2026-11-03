# Third-party notices

The MIT license covers original nanoCAD MCP code and documentation. It does not replace the licenses of the components below. Retain the upstream notices when redistributing the corresponding files.

| Component | Terms and notices |
|---|---|
| .NET runtime 10.0.11 and Microsoft .NET libraries | [Upstream license](licenses/dotnet-LICENSE.txt), [runtime third-party notices](licenses/dotnet-THIRD-PARTY-NOTICES.txt), [Microsoft.Extensions notices](licenses/microsoft-extensions-THIRD-PARTY-NOTICES.txt) |
| ModelContextProtocol and ModelContextProtocol.Core 2.2.0 | Copyright Model Context Protocol a Series of LF Projects, LLC. Package metadata declares Apache-2.0. The [upstream license file](licenses/modelcontextprotocol-LICENSE.txt) also preserves the project's MIT-to-Apache transition notice and applicable MIT terms. The libraries are distributed without modification. |
| nanoCAD and the nanoCAD SDK | Separate proprietary terms. A nanoCAD installation and the required module licenses are prerequisites. The public source snapshot and release do not include the nanoCAD SDK or host assemblies. |

Dependency identities are recorded in the server's `.deps.json`. Developers can inspect resolved NuGet metadata in `obj/project.assets.json` after restore. Upstream license files are included separately from the project's own MIT license.
