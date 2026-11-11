# NOF development

[简体中文](../zh-CN/README.md) · [Shared development standards](../../.standards/docs/en/README.md)

NOF is maintained by the repository maintainers, with XmmShp as the current
maintainer and acceptance owner. Maintainers designate a backup and the relevant
domain reviewers before delivery. The adopted standards revision is the exact
commit recorded by the `.standards` Git submodule; do not follow an upstream branch
during verification or release.

Use the stable .NET SDK and workload set in `global.json`. Runtime projects target
.NET 11; compiler generators retain `netstandard2.0`. Follow [the framework rules](../../.agents/rules/nof-dev.md)
for formatting, restore, build and test commands. Clone with `--recurse-submodules`
or run `git submodule update --init --recursive`, then run
`python .standards/.ci/check-adoption.py` after the adoption commit is recorded.

Work starts in an issue and a lowercase topic branch such as
`feature/2-result-unions`. Refresh `main`, preserve existing topic work and submit
a pull request (a proposed change for independent review) to `main`. Ordinary
changes use a Conventional Commit squash message. Breaking changes document
compatibility and migration in the change request and final commit body.

Required checks are the standards/contribution check and the Windows/Linux build
and test jobs. Linux also verifies formatting and Native AOT (compilation to a
native executable). Passing checks do not authorize a merge. The maintainer must
authorize the exact candidate and obtain the independent approvals required by
[REV-02](../../.standards/docs/en/01-governance/review-standards.md); breaking API and
authorization changes require two independent qualified reviewers, including a
domain owner. Configure equivalent protected-branch gates before merging if a
hosting feature cannot enforce these rules directly.

The existing `main` delivery workflow publishes uniquely versioned nightly NuGet
packages after validation. Stable releases use explicit version tags or the
documented manual workflow. Existing published versions, including `v11.0.0`, are
immutable. Future protected release tags require verified signatures. No tag or
stable release is created merely to validate a topic branch.

See [contributing](../../CONTRIBUTING.md), [architecture](../design/index.md),
[security reporting](../../SECURITY.md), and the [Apache 2.0 license](../../LICENSE).
