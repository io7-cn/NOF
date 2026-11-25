# NOF development

[简体中文](../zh-CN/README.md) · [Shared development standards](../../.standards/docs/en/README.md)

NOF is maintained by the repository maintainers, with XmmShp as the current
maintainer and acceptance owner. Maintainers designate a backup and the relevant
domain reviewers before delivery. The adopted standards revision is the exact
commit recorded by the `.standards` Git submodule; do not follow an upstream branch
during verification or release.

Use the stable .NET SDK and workload set in `global.json`. Runtime projects target
.NET 11; compiler generators retain `netstandard2.0`. Follow [the framework rules](https://github.com/io7-cn/NOF/blob/main/.agents/rules/nof-dev.md)
for formatting, restore, build and test commands. Clone with `--recurse-submodules`
or run `git submodule update --init --recursive`, then run
`python .standards/.ci/check-adoption.py` after the adoption commit is recorded.

Work starts in an issue and a lowercase topic branch such as
`feature/2-result-unions`. Refresh `main`, preserve existing topic work and submit
a pull request to `main`. Ordinary
changes use a Conventional Commit squash message. Breaking changes document
compatibility and migration in the change request and final commit body.

Required checks are the standards/contribution check and the Windows/Linux build
and test jobs. Linux also verifies formatting and Native AOT (compilation to a
native executable). The maintainer authorizes the exact candidate after considering
verification and the independent local review recommended by
[REV-02](https://github.com/io7-cn/standards/blob/415e8f1c2acde55062a42bcd6757d87f71b7a57c/docs/en/01-governance/review-standards.md#standards).
Seek domain-owner input for high-risk changes and review failure and recovery paths.
GitHub protects `main`, including administrators, and requires both OS checks,
`Standards and contribution policy`, resolved conversations and an up-to-date branch.
The required review count is zero under REV-04. Force pushes and deletion are disabled,
and merging is restricted to XmmShp.

The existing `main` delivery workflow publishes uniquely versioned nightly NuGet
packages after validation. Stable releases use explicit version tags or the
documented manual workflow. Existing published versions, including `v11.0.0`, are
immutable. Future protected release tags require verified signatures. No tag or
stable release is created merely to validate a topic branch.

See [contributing](https://github.com/io7-cn/NOF/blob/main/CONTRIBUTING.md), [architecture](design/index.md),
[security reporting](https://github.com/io7-cn/NOF/blob/main/SECURITY.md), and the [Apache 2.0 license](https://github.com/io7-cn/NOF/blob/main/LICENSE).


## Guides

- [Framework overview](overview.md)
- [Contributing](contributing.md)
- [Code and documentation organization](development/code-organization.md)
- [Architecture and migration guides](design/index.md)
- [Package guides](packages/index.md)
- [Interactive Sample](design/sample-workbench.md)
