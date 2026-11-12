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
a pull request (a proposed change for independent review) to `main`. Ordinary
changes use a Conventional Commit squash message. Breaking changes document
compatibility and migration in the change request and final commit body.

Required checks are the standards/contribution check and the Windows/Linux build
and test jobs. Linux also verifies formatting and Native AOT (compilation to a
native executable). Passing checks do not authorize a merge. The maintainer must
authorize the exact candidate and obtain the independent approvals required by
[REV-02](https://github.com/io7-cn/standards/blob/d719e4fecda60102c81d8266e3456b836417c455/docs/en/01-governance/review-standards.md); breaking API and
authorization changes require two independent qualified reviewers, including a
domain owner. GitHub protects `main`, including administrators: it requires both OS checks,
`Standards and contribution policy`, two approvals, approval after the latest push,
resolved conversations and an up-to-date branch. Force pushes and deletion are
disabled; merging is restricted to XmmShp. Approval identities must still satisfy
REV-02; the numeric approval count alone does not establish reviewer qualification.

The existing `main` delivery workflow publishes uniquely versioned nightly NuGet
packages after validation. Stable releases use explicit version tags or the
documented manual workflow. Existing published versions, including `v11.0.0`, are
immutable. Future protected release tags require verified signatures. No tag or
stable release is created merely to validate a topic branch.

See [contributing](https://github.com/io7-cn/NOF/blob/main/CONTRIBUTING.md), [architecture](../design/index.md),
[security reporting](https://github.com/io7-cn/NOF/blob/main/SECURITY.md), and the [Apache 2.0 license](https://github.com/io7-cn/NOF/blob/main/LICENSE).

- [Result Union migration](design/result-unions.md)
- [Application core and adapters (ADR 0001)](design/0001-application-core-and-adapters.md)
- [Application request implementation guide](design/application-requests.md)

- [Configuration node REST implementation](design/configuration-rest.md)

- [Declared expected failures](design/expected-failures.md)
