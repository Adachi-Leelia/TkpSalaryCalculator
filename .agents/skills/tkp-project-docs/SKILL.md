---
name: tkp-project-docs
description: Consult the TkpSalaryCalculator repository documentation before planning, implementing, modifying, reviewing, or testing requirements, UI behavior, salary calculations, settings, data models, database changes, migrations, or acceptance behavior. Use for any repository task whose answer or changes may be constrained by docs/, and when resolving ambiguity or checking consistency among code, tests, and specifications.
---

# Use TKP Project Documentation

## Workflow

1. Resolve the repository root with `git rev-parse --show-toplevel`. If Git is unavailable, walk upward from the current working directory until finding `docs/`.
2. Inventory the current documentation with `rg --files <repo-root>/docs`. Do this on every task so newly added documents are not missed.
3. Before proposing a design or editing code, read the documents relevant to the task. Start with `docs/requirements.md` for new or cross-cutting behavior, then read every affected specification.
4. Search large documents for relevant terms, requirement IDs, screen IDs, table names, and acceptance criteria with `rg -n`. Read the surrounding sections, not only matching lines.
5. Treat explicit requirements and accepted ADRs as constraints. Preserve stated scope, invariants, defaults, terminology, and unresolved items.
6. If documents conflict or do not determine the requested behavior, identify the exact files and sections involved. State the gap and any necessary assumption instead of silently inventing a rule.
7. After making changes, verify the implementation and tests against the relevant requirements and acceptance criteria. In the final response, name the documents that materially informed the result and report any remaining mismatch or open question.

## Document map

- `docs/requirements.md`: product scope, functional and non-functional requirements, calculation principles, use cases, and acceptance criteria.
- `docs/default_setting.md`: default service configuration values.
- `docs/screen_specification.md`: navigation, screens, interactions, dialogs, UI states, and accessibility.
- `docs/setting_history_data_model.md`: settings snapshots, effective months, history behavior, integrity rules, and import/export behavior.
- `docs/database_specification.md`: SQLite schema, constraints, indexes, transactions, migration rules, and export format.
- `docs/test_specification.md`: required test coverage, expected cases, environments, traceability, and release criteria.
- `docs/adr/*.md`: accepted architectural decisions and their consequences. Read every ADR relevant to the affected area.

Do not copy these specifications into the skill. Always read the repository files so the task uses their latest version.
