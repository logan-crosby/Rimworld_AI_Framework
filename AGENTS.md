# Repository Guidelines

## Project Structure & Module Organization

`Rim_AI_Framework.sln` contains the main RimWorld mod project. Runtime code lives under `RimAI.Framework/Source/`, organized by responsibility: `API`, `Configuration`, `Core`, `Execution`, `Shared`, `Translation`, and `UI`. Public request, response, tooling, and result models belong in `RimAI.Framework.Contracts/Models/`; keep this project lightweight because other mods consume it.

Static mod content is stored in `RimAI.Framework/About/`, `Languages/`, and `loadFolders.xml`. Current architecture and API documentation lives in `docs/`; `docs/old/` is historical reference, not the preferred place for updates. Compiled assemblies are written to `RimAI.Framework/Assemblies/`.

## Build, Test, and Development Commands

- `dotnet restore Rim_AI_Framework.sln` — restore NuGet dependencies.
- `dotnet build Rim_AI_Framework.sln -c Debug` — compile the framework and referenced contracts project.
- `dotnet build Rim_AI_Framework.sln -c Release` — create release assemblies.
- `RIMWORLD_DIR=/path/to/RimWorld dotnet build Rim_AI_Framework.sln` — override RimWorld detection.

The main project’s post-build target deletes and recreates the deployed `Mods/RimAI_Framework` directory. Confirm `RIMWORLD_DIR` before building if RimWorld is installed in a nonstandard location.

## Coding Style & Naming Conventions

Use four-space indentation and standard C# conventions: PascalCase for types, methods, and public members; camelCase for parameters and locals; and `I` prefixes for interfaces. Match namespaces to directories. Keep asynchronous methods suffixed with `Async`, use descriptive exception and logging messages, and add XML documentation to public APIs. Both projects target .NET Framework 4.7.2 with the latest configured C# language version.

## Testing Guidelines

No automated test project is currently committed. Every change must build cleanly and be validated in RimWorld, including settings UI, success and failure paths, and relevant mod combinations. New test projects should use the `*.Tests` suffix and deterministic tests that mock external LLM services.

## Commit & Pull Request Guidelines

Prefer concise Conventional Commit subjects such as `feat: add provider template`, `fix: handle failed streaming requests`, or `docs: update API guide`. Keep each commit focused.

Pull requests should describe behavior changes, list validation performed, link related issues, and include screenshots for UI changes. Update English and translated documentation or language files when user-facing behavior changes; never commit API keys, local settings, or generated build output.

<!-- BEGIN BEADS INTEGRATION v:1 profile:minimal hash:ca08a54f -->
## Beads Issue Tracker

This project uses **bd (beads)** for issue tracking. Run `bd prime` to see full workflow context and commands.

### Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work
bd close <id>         # Complete work
```

### Rules

- Use `bd` for ALL task tracking — do NOT use TodoWrite, TaskCreate, or markdown TODO lists
- Run `bd prime` for detailed command reference and session close protocol
- Use `bd remember` for persistent knowledge — do NOT use MEMORY.md files

## Session Completion

**When ending a work session**, you MUST complete ALL steps below. Work is NOT complete until `git push` succeeds.

**MANDATORY WORKFLOW:**

1. **File issues for remaining work** - Create issues for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **PUSH TO REMOTE** - This is MANDATORY:
   ```bash
   git pull --rebase
   bd dolt push
   git push
   git status  # MUST show "up to date with origin"
   ```
5. **Clean up** - Clear stashes, prune remote branches
6. **Verify** - All changes committed AND pushed
7. **Hand off** - Provide context for next session

**CRITICAL RULES:**
- Work is NOT complete until `git push` succeeds
- NEVER stop before pushing - that leaves work stranded locally
- NEVER say "ready to push when you are" - YOU must push
- If push fails, resolve and retry until it succeeds
<!-- END BEADS INTEGRATION -->
