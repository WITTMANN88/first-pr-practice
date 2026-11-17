# Reforger MCP setup

PowerShell scripts that install the MCP servers and skills for Arma Reforger modding
(Enfusion, Workbench, Blender, Substance Painter, Obsidian, Discord) into Claude Code and Cursor.

| File | Purpose |
|---|---|
| `Install-Prereqs-D.ps1` | Installs Git, Node.js, uv, VS Build Tools, Rust, Blender and Obsidian to `D:\Apps` |
| `Install-ReforgerMcp.ps1` | Installs and registers the MCP servers and skills |

Run both from a regular (non-administrator) Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Prereqs-D.ps1 -OpenSteam
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-ReforgerMcp.ps1 -InstallRoot D:\mcp -AssetsRoot D:\ReforgerAssets -WithCursor -WithDiscord
```

Every third-party component is pinned to a reviewed commit or checksum; see the comments at the top of each script.
