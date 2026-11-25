# Contributing to NOF

[简体中文](../zh-CN/contributing.md)

Owner: NOF maintainers (XmmShp). Read the exact [adopted standards](../../.standards/docs/en/README.md) and [development profile](README.md). Be respectful and constructive; report reproducible behavior with minimal examples and no private data.

## Environment and workflow

Clone with submodules, use global.json and install applicable MAUI/Android workloads. On this repository's designated Windows development machine, dotnet commands run elevated. Frontend formatting uses Node 22 and the locked Prettier dependency; use npm ci --ignore-scripts.

Inspect the working tree, refresh main and create/reuse a lowercase topic branch such as refactor/4-code-layout. Preserve unrelated changes. Never edit, commit or push directly to protected branches. Link the issue with a reference or a GitHub closing directive when merge satisfies its acceptance criteria. Use a Conventional Commit subject and describe breaking changes and migration. Push the topic branch and open a pull request (PR) against main. Keep broad format-only cleanup separately reviewable from behavior changes.

## Code and documentation

Use the shared EditorConfig/Prettier baseline, public classes by default, file-scoped namespaces, Allman control blocks and explicit cancellation. Add XML documentation to public library APIs. Preserve dependency direction and published names; follow [file organization](development/code-organization.md). Source generators inspect declarations/referenced metadata independently, not other generators' outputs.

Update English and Simplified Chinese documentation together with reciprocal links. Package READMEs are short tool-discoverable portals; full usage belongs in docs/en/packages and docs/zh-CN/packages. Do not translate obsolete APIs as current advice. Preserve historical context in decisions and point to the current design. Keep screenshots, per-run reports and temporary scripts under ignored artifacts or linked external review evidence.

## Verification

Build generator assemblies before formatting so the formatter sees the same Release outputs. Stop when a required check fails, fix it and rerun the affected checks.

```sh
dotnet restore NOF.slnx -p:Configuration=Release
dotnet build NOF.slnx -c Release --no-restore
dotnet format NOF.slnx --no-restore --verify-no-changes
dotnet test NOF.slnx -c Release --no-build --no-restore
npm ci --ignore-scripts
npm run check:frontend
python .ci/check-documentation.py
python .standards/.ci/check-adoption.py
python .ci/check-contribution.py
```

Set Configuration=Release for formatting. Run relevant native persistence tests and EF pending-model checks when changing models; CI also validates Native AOT on Linux. Run optional RabbitMQ tests in a separate virtual host, never against the live demonstration queues. Record results in the PR/CI artifacts, not versioned acceptance reports.

## Review and delivery

Use the approved contributor identity and a Conventional Commit squash message. Pass the required Windows/Linux CI and standards checks and resolve blocking discussions before merge. Seek independent local review, request relevant domain-owner input for high-risk changes and follow up after material updates. Record findings and verification against the reviewed candidate in the PR. The maintainer considers that evidence and authorizes the merge. Use the authorized nightly and stable publication workflows and preserve published versions. Reviewers should be able to assess the problem, behavior, migration and verification from the PR.
