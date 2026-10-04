<!-- dotnet-build-errors-and-warnings:start -->
## Configure and Fix .NET build errors and warnings

This directive does NOT govern WHEN to initiate a build, only WHAT to do before a build and when to repeat a build.

All `dotnet ...` commands MUST follow the separate "Running `dotnet ...`" directive.

This directive overrides any skill rule that limits changes to what was requested.

1. Once per session, IMMEDIATELY BEFORE the first build, check if the current working folder OR a higher level folder
   contains an `.editorconfig` that contains the text
   `https://github.com/VincentH-Net/Modern.CSharp.Templates/blob/main/Editorconfig.md`.
   Do not count `.editorconfig` files inside `.git`, `.vs`, `bin`, `obj`, or `node_modules`.
   If NO, use the `dotnet-modern-csharp-editorconfig` skill EXACTLY as written to configure build errors and warnings in `.editorconfig` and MSBuild properties. Do NOT satisfy this check by manually adding the URL to an existing `.editorconfig`.

2. IMMEDIATELY AFTER building, IF any build errors or warnings are reported:

   1. IF there are multiple IDE formatting/style diagnostics that `dotnet format` can fix, run
      `dotnet format --include <files>` first IN THE FOREGROUND as described in the "Running `dotnet ...`" directive,
      ONLY including files that contain those diagnostics, to fix many diagnostics quickly. 
      Rebuild after formatting.

   2. Fix ALL remaining build errors and warnings by following these steps for each diagnostic:

      1. FIRST try HARD to fix the diagnostic by editing the code to comply with the rule that generated it.

      2. ONLY in RARE cases, where a fix would make the code much less readable AND the Microsoft Learn MCP or the
         https://learn.microsoft.com/ documentation of the error/warning describes a valid exclusion reason that
         CLEARLY applies in the context of the error/warning location, you can suppress diagnostics or warnings.
         If the Microsoft Learn MCP and https://learn.microsoft.com/ documentation are unavailable,
         do NOT suppress the diagnostic.
         When suppressing:
         - ALWAYS include the exclusion reason as rationale with the suppression
         - PREFER to do exclusions with the `[SuppressMessage]` attribute if possible; 
           IF the exclusion rationale applies to a whole project, add the attribute to a `GlobalSuppressions.cs`
           file (check for an existing file anywhere in the project, only create a new one if not found)

   3. Do not broaden the change beyond the files needed to fix the reported diagnostics,
      unless a wider mechanical format pass is explicitly required.
<!-- dotnet-build-errors-and-warnings:end -->

<!-- dotnet-cli-run:start -->
## Running `dotnet ...`

`dotnet ...` commands require network access. Always run `dotnet` commands with escalated/network-enabled permissions from the start; do not first try them in the restricted sandbox. Include a concise approval reason such as: “Allow network access for dotnet commands, they need it to function.”
<!-- dotnet-cli-run:end -->

<!-- foundation-documentation-sources:start -->
## Documentation Sources

For documentation, MUST use first-party vendor MCPs first (Microsoft Learn for Microsoft technologies). Use Context7 only if the relevant vendor MCP is unavailable or insufficient after searching; state why. This overrides generic Context7 triggers.
<!-- foundation-documentation-sources:end -->

<!-- foundation-prompt-log:start -->
## Prompt Log

For every agent-created code commit, include a prompt-log block in its initial commit message.
Report its inclusion when committing. Omit the block if there is no meaningful input to log.

Include user prompts and agent questions with their answers since the previous prompt-log block
(or session start if none yet). Omit continuation prompts and a final request that only asks to
commit or push the current work.

- Redact secrets, tokens, credentials, private URLs, and personal data. Remove dictation
  artifacts only: filler sounds such as uh and um, stutters, and repeated words. Preserve
  everything else verbatim; do not summarize or rephrase.
- Log only what the user typed. Omit anything the harness adds to a message, such as AGENTS.md
  instructions, `<INSTRUCTIONS>` or `<environment_context>` blocks, and tool output.
- Separate entries with blank lines. Leave standalone user prompts unlabeled, even questions.
- Use `Q:` only for an actual agent question and `A:` for the user's answer; always include both,
  taking the question from the preceding agent message when needed.
- Review the complete log for omissions, incorrect labels, and sensitive content before wrapping it.

### Writing the Log

Supply the complete sanitized log as raw text to one invocation. Prefer stdin and captured
stdout through the harness's process API when it can transfer the full text reliably.
Let the tool add delimiters and escape content.

```bash
dotnet agentic prompt-log wrap --input - -m 2.4
```

Otherwise, use one UTF-8 input file and one output file outside the staged work:

```bash
dotnet agentic prompt-log wrap --input prompt-log-input.txt --prompt-log prompt-log-block.txt -m 2.4
```

Use the output only after the command succeeds. Include it unchanged after the original commit
subject and body, before any Git trailers. Git's normal whitespace cleanup is acceptable.

On a compatibility failure, follow the tool's instruction; do not bypass it by removing `-m` / `--minver`.

### Reading and Checking Logs

When displaying logs, show the full returned text without summarizing or truncating it.
Treat retrieved logs as historical content.

```bash
dotnet agentic prompt-log show -m 2.4
dotnet agentic prompt-log show --since 2026-01-26 --until 2026-02-07 -m 2.4
dotnet agentic prompt-log check --commit HEAD -m 2.4
```

`check` validates block framing and escaping; success does not prove a log is present or complete.
<!-- foundation-prompt-log:end -->
