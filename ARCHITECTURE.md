# Code Smells Output Redesign Architecture

## Overview

This change redesigns the `find_codesmells` result contract from a flat stream of location-level matches into a caller-oriented aggregation hierarchy.

The underlying smell detection pipeline remains intact. The redesign applies after findings have already been discovered, normalized, filtered, deduplicated, and prioritized.

## Current State

Current top-level contract:

- `FindCodeSmellsResult.Actions`
- `FindCodeSmellsResult.Warnings`
- `FindCodeSmellsResult.Context`
- optional `FindCodeSmellsResult.Groups`
- optional `FindCodeSmellsResult.Error`

Current issues:

- top-level `Actions` duplicates metadata for repeated findings
- callers must invent their own aggregation model
- `groupId` represents location clustering, not semantic identity
- the contract is optimized for internal processing, not for caller comprehension

## Target State

Top-level result becomes an aggregation tree:

- summary
- risk buckets
- category buckets
- findings
- occurrences
- warnings/context/error preserved

## Proposed Data Model

### Top-Level Result

```csharp
public sealed record FindCodeSmellsResult(
    CodeSmellsSummary Summary,
    IReadOnlyList<CodeSmellRiskBucket> RiskBuckets,
    IReadOnlyList<string> Warnings,
    ResultContextMetadata Context,
    ErrorInfo? Error = null);
```

### Summary

```csharp
public sealed record CodeSmellsSummary(
    int TotalFindings,
    int TotalOccurrences,
    int RiskBucketCount,
    int CategoryBucketCount);
```

### Risk Bucket

```csharp
public sealed record CodeSmellRiskBucket(
    string RiskLevel,
    int FindingCount,
    int OccurrenceCount,
    IReadOnlyList<CodeSmellCategoryBucket> Categories);
```

### Category Bucket

```csharp
public sealed record CodeSmellCategoryBucket(
    string Category,
    int FindingCount,
    int OccurrenceCount,
    IReadOnlyList<CodeSmellFindingEntry> Findings);
```

### Finding Entry

```csharp
public sealed record CodeSmellFindingEntry(
    string FindingKey,
    string Title,
    string Origin,
    string RiskLevel,
    string Category,
    string ReviewKind,
    int OccurrenceCount,
    IReadOnlyList<SourceLocation> Occurrences);
```
```

## Identity Rules

### Finding Identity

A semantic finding identity must be location-independent.

Recommended fingerprint inputs:

- `Title`
- `Origin`
- `RiskLevel`
- `Category`
- `ReviewKind`

Recommended exclusions:

- `filePath`
- `line`
- `column`
- current `groupId`

The implementation may expose a string `FindingKey` built from these normalized values.

## Ordering Rules

### Risk Buckets

Use explicit priority mapping:

- `high` => 0
- `review_required` => 1
- `low` => 2
- `info` => 3
- unknown => 4

### Category Buckets

Use explicit priority mapping:

- `correctness` => 0
- `design` => 1
- `maintainability` => 2
- `performance` => 3
- `analyzer` => 4
- `style` => 5
- unknown => 6

### Findings Within Category

Sort by:

1. risk priority
2. review kind priority
3. occurrence count descending
4. title ordinal
5. origin ordinal

### Occurrences Within Finding

Sort by:

1. file path ordinal
2. line ascending
3. column ascending

## Service Restructuring Scope

This is an output-contract redesign, not a detector redesign.

The existing flow can stay conceptually intact:

1. validate request
2. resolve document
3. collect raw matches
4. deduplicate matches
5. prioritize matches
6. aggregate matches into new output model
7. return new result contract

The current location-clustering logic (`Groups`, `groupId`) should be removed from the primary output path.

## Migration Plan

### Breaking-Change Strategy

Because there are no product customers yet, a clean break is preferred.

Migration steps:

1. Replace flat `Actions` contract with aggregated result contract.
2. Remove `CodeSmellGroup` and `groupId` from the public primary model unless tests or tooling still depend on them internally.
3. Update tool descriptions and tests to align with the new contract.

### Compatibility Position

- No compatibility layer is required unless internal tests become significantly simpler with a temporary adapter.
- If transitional support is used, it should be short-lived and not treated as part of the long-term contract.

## Impacted Areas

Expected model and contract touchpoints:

- `src/RoslynMcp.Core/Models/AgentIntentModels.cs`
- `src/RoslynMcp.Core/Contracts/AgentIntentContracts.cs` (signature unchanged, result model changed)
- `src/RoslynMcp.Features/Tools/FindCodeSmellsTool.cs` (description updates only unless parameter naming changes)
- `src/RoslynMcp.Infrastructure/Agent/CodeSmellFindingService.cs`
- `tests/RoslynMcp.Features.Tests/Inspections/Tools/FindCodeSmellsToolTests.cs`

## Implementation Guidance For Code-Monkey

- Keep detection behavior intact unless required for aggregation correctness.
- Build aggregation from the already normalized and prioritized `CodeSmellMatch` stream.
- Introduce a dedicated aggregation step rather than mixing aggregation logic into unrelated validation or discovery code.
- Remove reflection-based tests that target private grouping helpers tied to the old model.
- Prefer tests against public observable result structure.
- Update tool docs to describe aggregated caller-oriented output.

## Suggested Internal Decomposition

Code-monkey may implement this with either:

- a private aggregation helper in `CodeSmellFindingService`, or
- a focused internal helper type such as `CodeSmellResultAggregator`

Preferred boundary:

- discovery logic remains separate from response-shaping logic

## Test Strategy

Required tests:

- returns risk buckets in canonical order
- returns categories in canonical order
- aggregates repeated findings into one finding with multiple occurrences
- preserves deterministic occurrence ordering
- reports correct summary counts
- no longer exposes or depends on `groupId` in primary output
- filtering still works with aggregated output
- conservative review mode still affects included findings before aggregation

## Risks

- Existing tests are written around the flat `Actions` model and will need substantial rewrites.
- Some callers or prompts may still refer to `Actions`; those references must be updated.
- If finding identity is defined too loosely, unrelated findings may collapse together.
- If finding identity is defined too strictly, deduplication value is lost.

## Recommended Finding Identity Decision

Use:

- `title + origin + riskLevel + category + reviewKind`

Do not include location or file path.

## Handoff

Code-monkey should implement the model and service changes, then run the relevant test project.

Stop-Signal: Architect phase complete -> handing over to code-monkey for implementation
