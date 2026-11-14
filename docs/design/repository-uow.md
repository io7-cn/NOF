# Repository and unit of work

Persistence now uses domain-owned aggregate repositories, Application DTO query ports and
IUnitOfWork. The generic core ORM facade is removed. Infrastructure message stores retain
atomic delivery operations while native providers own tracking and transactions.

Read the complete [English migration guide](../en/design/persistence-boundaries.md) or
[简体中文](../zh-CN/design/persistence-boundaries.md).
