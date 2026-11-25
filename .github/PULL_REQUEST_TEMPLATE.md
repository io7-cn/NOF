## Description

<!-- What does this PR do? Why is it needed? -->

## Related Issues

<!-- Reference related issues with Refs: #123 or an issue link. Use a closing directive such as Closes #123 when merge satisfies the agreed acceptance criteria. -->

## Type of Change

- [ ] Bug fix (non-breaking change that fixes an issue)
- [ ] New feature (non-breaking change that adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to change)
- [ ] Documentation update
- [ ] Refactoring (no functional changes)
- [ ] CI/CD or tooling change

## Checklist

<!-- For both human developers and AI agents: check ALL applicable items. Do NOT only update source code. -->

### Build & Quality

- [ ] Branch follows the adopted naming rules and is current with `main`
- [ ] Standards adoption and contribution-policy checks pass
- [ ] `dotnet format --verify-no-changes` passes
- [ ] `dotnet build --configuration Release` succeeds with no warnings
- [ ] `dotnet test` passes
- [ ] Commit messages follow [conventional commits](../CONTRIBUTING.md#commit-conventions)

### Code Completeness

- [ ] New public APIs have XML documentation
- [ ] Tests added/updated for behavioral changes
- [ ] Sample application updated if APIs changed (`sample/` must still compile)

### Documentation & Tooling

- [ ] `README.md` updated (if public API surface, packages, or architecture changed)
- [ ] `CONTRIBUTING.md` updated (if conventions or processes changed)
- [ ] `docs/` updated (if DocFX API docs affected)
- [ ] CI/CD workflows updated (`.github/workflows/` — if new packages or test projects added)
- [ ] AI agent instructions updated (`.agents/` rules/workflows/skills and `.github/copilot-instructions.md`)

### Compatibility and review

- [ ] Breaking impact and migration examples are documented where applicable
- [ ] Verification evidence identifies the exact candidate and any unverified scope
- [ ] Rollout and rollback implications are recorded
- [ ] Local review findings, when available, and maintainer merge authorization refer to the current candidate

<!-- Seek relevant domain-owner input for high-risk changes under REV-02 and review failure and recovery paths. The maintainer authorizes the merge after considering review and verification. -->
