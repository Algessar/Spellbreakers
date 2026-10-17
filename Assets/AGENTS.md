## Documentation

`Assets/DEV/` contains the project's design and technical documentation.

Treat current DevDoc material as project context, but prefer the actual source code and Unity assets when determining
how the current implementation behaves.

Do not assume archived or explicitly obsolete documentation represents the current design.

If a document begins with `[obsolete]` or `ai-ignore`, skip it completely.
If a document begins with `[under revision]`, it is not canonical and should be treated with caution.

## Task Boundaries

- If asked for a plan, summary, analysis, assessment, or description of the current project state, do not modify any files or implement changes.
- Planning and implementation are separate tasks. Approval of a plan does not authorize implementation unless I explicitly request that implementation begin.
- When asked to assess the current implementation against design documentation, inspect the relevant code and documents, identify gaps, and estimate the scope of required changes without making modifications.

Example: "What is the combat system currently capable of, and approximately how extensive a refactor would it need to comply with what is written in Enemy Combat.md?"

This is an assessment request, not an implementation request.