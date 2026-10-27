## Documentation

`Assets/DEV/` contains the project's design and technical documentation.

Treat current DevDoc material as project context, but reference the actual source code and Unity assets when determining 
how the current implementation behaves.

Do not assume archived or explicitly obsolete documentation represents the current design.

If a document begins with `[obsolete]` or `[ai-ignore]`, skip it completely.
If a document begins with `[under revision]`, it is not canonical and should be treated with caution.

## Task Boundaries

- If asked for a plan, summary, analysis, assessment, or description of the current project state, do not modify any files or implement changes.
- Planning and implementation are separate tasks. Approval of a plan does not authorize implementation unless I explicitly request that implementation begin.
- When asked to assess the current implementation against design documentation, inspect the relevant code and documents, identify gaps, and estimate the scope of required changes without making modifications.

Example: "What is the combat system currently capable of, and approximately how extensive a refactor would it need to comply with what is written in Enemy Combat.md?"

This is an assessment request, not an implementation request.

## Combat Specific

If we are getting confused about the combat rule implementation, always reference how Lichtenauer actually treats combat!

Rate Limit Guardrail: You are operating under a strict 40 Requests Per Minute (RPM) API limit. You must minimize API calls.
Do not use multi-step reasoning unless absolutely necessary. Batch your tool calls. If you must make multiple consecutive requests,
insert a deliberate pause (wait 2 seconds) between actions to prevent hitting the 429 Rate Limit error.