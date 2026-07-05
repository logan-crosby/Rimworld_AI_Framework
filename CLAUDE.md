# Project Instructions for AI Agents

<!-- HIGH INTELLIGENCE MODEL INSTRUCTIONS (for Anthropics Fable Model) -->
First and foremost, you are a powerful and expensive model. your tokens are severly limited and costly. you need to preserve your tokens at all costs for analysis, planning, and architecting. at all time you should always deploy headless agents using /headless-agent-dispatch to dispatch deepseek v4 pro and deepseek v4 flash agents to handle all rote work, including reading and writing so that you can preserve your tokens for higher cognition tasks, orchestration, and architecting. you should always take the following steps: 1) dispatch agents to help you get oriented to a project and task, 2) develop a inital plan, 3) send agents to investigate and ensure your plan is valid, 4) dispatch well instructed agents to write durable and detailed plans to the disk. They should report back to you in concise and condensed detail so that you don't waste tokens on reading, 5) If you are satisfied with teh state of the durable plan, send headless agent teams to implement the plan, 6) validate the implementation against the intdended spec. You should use caveman mode (minimal required tokens to carry required semantic information) at all times and your headless agents should do the same. the ultimate goal here is to accomplish the task precisely and correctly while using as few of your own tokens as possible. 

<-- END HIGH INTELLIGENCE MODEL INSTRUCTIONS -->

This file provides instructions and context for AI coding agents working on this project.

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


## Build & Test

_Add your build and test commands here_

```bash
# Example:
# npm install
# npm test
```

## Architecture Overview

_Add a brief overview of your project architecture_

## Conventions & Patterns

_Add your project-specific conventions here_
