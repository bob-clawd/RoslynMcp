# Code Smells Output Redesign

## Goal

Redesign the output contract of `find_codesmells` so callers receive an aggregated, priority-ordered result that is easier to consume than the current flat `Actions` list.

## Problem Statement

The current response returns a flat list of findings. For repeated findings at multiple locations, most fields are duplicated and only the location differs. This creates unnecessary output volume and forces the caller to perform grouping, counting, and prioritization itself.

## Objectives

- Replace the flat-first response shape with an aggregated result optimized for callers.
- Group findings first by `riskLevel`, then by `category`, then by finding identity.
- Preserve the concrete source locations where each finding occurs.
- Return results in deterministic, review-oriented order.
- Remove caller dependence on `groupId` for primary consumption.

## Non-Goals

- Do not improve the underlying detection engine in this change.
- Do not change how findings are discovered, normalized, or filtered unless required for the new contract.
- Do not add new smell categories or risk levels as part of this redesign.
- Do not optimize detection performance as a primary objective.

## Functional Requirements

### Response Shape

- The top-level result must expose aggregated output as the primary contract.
- The result must include a summary section with counts useful to callers.
- The result must include ordered risk buckets.
- Each risk bucket must include ordered category buckets.
- Each category bucket must include ordered finding entries.
- Each finding entry must include ordered occurrences.

### Grouping

- Findings must be grouped by normalized `riskLevel`.
- Within a risk bucket, findings must be grouped by normalized `category`.
- Within a category bucket, repeated findings must be grouped into a single finding entry when their semantic identity is the same.
- A finding's semantic identity must be location-independent.
- Occurrences must carry file path, line, and column.

### Ordering

- Risk buckets must be sorted from most important to least important.
- Category buckets must be sorted by review usefulness rather than alphabetically.
- Findings within a category must be sorted by importance and repetition density.
- Occurrences within a finding must be sorted deterministically.

### Counts

- The summary must provide overall counts for total findings and total occurrences.
- Each risk bucket must provide occurrence and finding counts.
- Each category bucket must provide occurrence and finding counts.
- Each finding entry must provide occurrence count.

### Contract Cleanup

- `groupId` must not be required by the new primary response shape.
- Breaking changes are acceptable.
- Legacy flat `Actions` output does not need to remain if it complicates the contract.

## Canonical Value Order

### Risk Level Priority

1. `high`
2. `review_required`
3. `low`
4. `info`

### Category Priority

1. `correctness`
2. `design`
3. `maintainability`
4. `performance`
5. `analyzer`
6. `style`

## Proposed Caller-Oriented Semantics

- `riskLevel` answers: how urgent is this?
- `category` answers: what kind of issue is this?
- `finding` answers: what exact issue repeats?
- `occurrences` answer: where does it happen?

## Acceptance Criteria

- A caller can understand the major issues in a file without re-grouping the response.
- Repeated findings appear once with multiple occurrences, not as duplicated top-level items.
- The highest-priority risk levels appear first.
- Categories appear in a fixed review-oriented order.
- The response remains deterministic for the same input.
- The new contract no longer requires `groupId` to express the primary information architecture.

## Open Design Constraints

- Existing normalized values for `riskLevel` and `category` remain authoritative for this iteration.
- The implementation may introduce a stable `findingKey` or equivalent semantic fingerprint.
- The implementation should preserve `warnings`, `context`, and `error` semantics unless there is a strong reason to improve naming consistency.

## Definition of Done

- Result models are updated to reflect the aggregated response shape.
- Service output is reorganized into the new hierarchy.
- Ordering rules are implemented and covered by tests.
- Tests validate grouping of repeated findings into a single finding entry with multiple occurrences.
- Tests validate absence of `groupId` from the new primary contract.
- Tool metadata and descriptions reflect the new caller-oriented output.

Architect phase complete only after `ARCHITECTURE.md` is also written and implementation tasks are clearly handed over.
