# CLAUDE.md

This file provides guidance to Claude Code when working in this workspace.

## Environment

- **Platform**: Windows
- **Workspace**: C:\Users\jeastaugh\source\repos\Experiments\5.next.project
- Use Windows-compatible commands (e.g., use backslashes in paths, no Unix-specific commands)

## Current Task

You are helping the user create a **requirements.md** file for their project. This happens in two phases.

**IMPORTANT: Do NOT implement the project. Do NOT write code, create source files, install packages, or build anything. Your ONLY job right now is to plan and write the requirements.md document. The actual implementation will happen later in a separate automated process.**

---

### Phase 1 — Discovery (do NOT write any files)

Start by asking the user to describe their project. Explore the full scope before writing anything:

- What does the project do? Who uses it?
- What are all the features and how do they connect?
- What are the user flows end-to-end?
- Any technology preferences or constraints?
- External dependencies, APIs, or services needed?
- What does "done" look like — what are the success criteria?
- Are there any edge cases or failure modes to handle?
- How should the project be tested? (unit tests, integration tests, e2e tests, specific frameworks?) — Claude will run these tests automatically during implementation to catch and fix bugs before the build is complete

Once you have a clear picture, **present a proposed task list** for the user to review. Describe each task briefly (title + one-line description) and ask if anything should be added, removed, or changed. Iterate on the proposal until the user is satisfied.

**Do NOT write any files during Phase 1.**

---

### Phase 2 — Write requirements.md (only when user confirms)

When the user explicitly says the plan is complete (e.g. "looks good", "go ahead", "create the requirements"), write all tasks at once to `C:\Users\jeastaugh\source\repos\Experiments\5.next.project\requirements.md`.

With full context of the whole plan, assign priorities and dependencies correctly:

```markdown
### TASK-001: Task title here
- **Status**: pending
- **Priority**: high
- **Dependencies**: none
- **Description**: Clear description of what needs to be done.

### TASK-002: Another task
- **Status**: pending
- **Priority**: medium
- **Dependencies**: TASK-001
- **Description**: This task depends on TASK-001 completing first.
```

### Rules

- Task IDs must be sequential: TASK-001, TASK-002, TASK-003, etc.
- Status should always be `pending` for new tasks
- Priority: `high`, `medium`, or `low`
- Dependencies: `none` or comma-separated task IDs (e.g., `TASK-001, TASK-002`)
- Keep descriptions clear and actionable
- **Do NOT create any files other than requirements.md** — no source code, no config files, no project scaffolding
