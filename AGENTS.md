# Repository Rules

- Follow the [English development standards](.standards/docs/en/README.md) at the exact submodule revision and the [English project documentation](docs/en/README.md).
- Follow the [Git branching standards](.standards/docs/en/02-development/git-branching.md) before editing. Inspect the branch and working tree, refresh the approved target, and create or reuse a compliant topic branch without discarding unrelated work. Do not edit, commit or push directly on protected branches. Merge only through a change request with required checks and independent approvals; administrative access and passing tests do not waive this workflow.
- Use lowercase topic branches such as `feature/2-result-unions`, `fix/3-timeout`, or `docs/4-onboarding`; include the issue number when practical. Target `main` and use a Conventional Commit squash message that describes breaking changes and migration.
- Keep project documentation in separate `docs/en/` and `docs/zh-CN/` files with reciprocal links. Keep this file English only.
- Keep temporary scripts, task evidence and per-run reports outside version control under [MNT-09](.standards/docs/en/05-maintenance/maintenance-standards.md#keep-acceptance-records-outside-source-repositories). Use the approved contributor identity for commits and link validation evidence from the issue or change request.

Read `.agents/rules/nof-dev.md` when contributing to the NOF framework.

## Class Accessibility

- Classes should be `public` by default throughout this repository.
- Do not mark a class `internal` unless there is a compelling, concrete reason that requires assembly-only visibility. Document that reason alongside the declaration.
- Being an implementation detail, a background service, or used only inside the repository is not sufficient justification for `internal`.

## Local Environment

This machine has a special local environment. Run every command that invokes `dotnet` with elevated permissions.
