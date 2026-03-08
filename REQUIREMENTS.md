# Requirements: code_inspection Agent Ergonomics and Semantic Reliability

## Context

`code_inspection` is already strong for C# semantic navigation, targeted impact analysis, and safe structural refactorings. The audit in `docs/TOOL_AUDIT.md` identifies several gaps that reduce reliability and agent usability in real workflows.

This document defines the required outcomes for the next improvement wave. It focuses on product behavior, tool contracts, and acceptance criteria. It does not prescribe implementation details.

## Problem Statement

The current toolset has four main weaknesses:

1. Symbol resolution by qualified name is too fragile for dependable automation.
2. Implementation and override discovery is not reliable enough for polymorphic C# code.
3. Call-flow output does not communicate static uncertainty explicitly enough.
4. Code-smell findings are too noisy and insufficiently prioritized for agent-driven review.

There is also a secondary ergonomics problem:

5. `symbolId` values are machine-usable but cumbersome for chaining and human inspection.

## Product Goals

The system shall:

- allow agents to address symbols through readable, robust inputs;
- provide trustworthy results for common C# polymorphism scenarios;
- distinguish clearly between certain static facts and inferred or incomplete results;
- emit code-quality findings that are useful for prioritization, not just raw analyzer noise;
- improve workflow efficiency for repeated multi-symbol or multi-file interactions.

## Primary Outcomes

### 1. Robust symbol addressing

Agents must be able to resolve types and members by readable identifiers, not only by file position.

### 2. Reliable polymorphism support

Agents must be able to discover interface implementations, abstract implementations, and virtual overrides with behavior that is consistent and predictable.

### 3. Explicit uncertainty communication

When the tool cannot know runtime truth from static analysis alone, it must say so in structured form.

### 4. Actionable quality signals

Quality findings must become easier to rank, filter, and consume safely in automated review flows.

### 5. Better automation ergonomics

Agents need more compact, stable, and readable handles for chaining tool calls and reasoning about outputs.

## Functional Requirements

### FR-1: Qualified-name symbol resolution

`resolve_symbol` shall support qualified-name lookup for:

- non-generic types;
- generic types using a readable generic notation;
- members using fully qualified member names;
- overloaded members using readable parameter signatures.

Examples of intended input shape:

```text
ProjectImpl.FastWorkItemOperation.ExecuteAsync(Guid, string, CancellationToken)
ProjectCore.OperationBase<TInput>
```

### FR-2: Structured ambiguity handling

When a symbol lookup is ambiguous, `resolve_symbol` shall return structured candidate results instead of only a generic failure outcome.

The response shall make it possible for an agent to:

- detect that ambiguity occurred;
- inspect candidate symbols;
- understand why disambiguation is needed.

### FR-3: Preserve existing position-based strength

All improvements to name-based lookup must preserve existing file-plus-position workflows. Position-based resolution remains a first-class supported path.

### FR-4: Reliable implementation discovery

`find_implementations` shall reliably return:

- concrete implementations of interface members;
- concrete implementations of abstract members;
- overrides of virtual members;
- implementations and overrides across project boundaries within the loaded solution.

### FR-5: Consistent polymorphic semantics

Where relevant, polymorphism-related tools shall use compatible semantics so that agents do not receive contradictory interpretations between:

- `find_implementations`;
- `find_callers`;
- `trace_call_flow`;
- hierarchy-related inspection tools.

### FR-6: Explicit uncertainty markers in flow analysis

`trace_call_flow` shall include structured uncertainty markers when a result reflects static approximation rather than confident target attribution.

The model shall support at least these categories:

- interface dispatch;
- polymorphic inference;
- reflection blind spot;
- dynamic dispatch blind spot;
- unresolved project attribution.

### FR-7: Optional possible-targets mode

The system should support an explicit mode for returning possible runtime targets when deterministic static attribution is not available.

This mode must be clearly distinct from direct, confirmed static targets.

### FR-8: Improved code-smell prioritization

`find_codesmells` shall better separate:

- style and trivia suggestions;
- maintainability findings;
- correctness and design risks;
- code-fix suggestions versus materially important review issues.

### FR-9: Grouped findings

`find_codesmells` should support grouped output so related findings in the same location or logical zone are not presented as an unstructured flood.

### FR-10: Agent-safe review mode

`find_codesmells` should support a more conservative mode that favors signal quality over completeness for automated or semi-automated review workflows.

### FR-11: Readable symbol handles

Tool responses should provide an additional symbol reference format that is:

- shorter than raw `symbolId`;
- stable within a workspace snapshot;
- readable enough for debugging and chaining;
- round-trippable back into supported tool inputs.

Raw `symbolId` remains supported for backward compatibility and machine precision.

### FR-12: Batch workflow support

The system should support batch-style operations for high-frequency workflows, including some or all of the following:

- resolving multiple symbols in one request;
- formatting multiple files in one request;
- collecting usages or callers for multiple symbols in one request;
- previewing refactoring impact before applying a mutation.

## Non-Functional Requirements

### NFR-1: Backward compatibility

Existing agent flows based on file position and current stable tool contracts must continue to work unless an explicit versioned contract change is introduced.

### NFR-2: Deterministic machine consumption

Outputs added for ambiguity, uncertainty, prioritization, and handles must be structured and deterministic enough for automated consumption.

### NFR-3: Honest confidence boundaries

The system must not imply runtime certainty where only static approximation exists.

### NFR-4: Usability for agents and humans

Responses should remain inspectable by humans while still being reliable for tool chaining and automation.

### NFR-5: No regression in solution-scale behavior

Improvements must remain practical for multi-project solutions and cross-project analysis.

## Non-Goals

The following are explicitly out of scope for this change set:

- full reconstruction of runtime behavior in reflection-heavy systems;
- complete understanding of dynamic dispatch beyond static evidence;
- replacing `read` for local code understanding;
- replacing `bash` for build, restore, runtime, or test validation;
- turning smell analysis into a perfect autonomous code-review authority.

## Acceptance Criteria

### AC-1: Name-based resolution

An agent can resolve a representative set of symbols by qualified name, including:

- a concrete type;
- a generic type;
- a non-overloaded member;
- an overloaded member using readable parameter signature syntax.

### AC-2: Ambiguity output

Ambiguous lookups return structured candidate information rather than an opaque not-found result.

### AC-3: Override discovery

For a virtual base member with known overrides in the solution, `find_implementations` returns the overriding members.

### AC-4: Explicit uncertainty

For interface-based and reflection-adjacent flow cases, call-flow results expose uncertainty markers that an agent can inspect programmatically.

### AC-5: Smell prioritization

In a mixed-quality file, trivial style findings no longer dominate the most important output in the default or agent-safe review path.

### AC-6: Handle ergonomics

Responses include a shorter or more readable symbol reference that can be reused in follow-up tool calls without losing correctness.

### AC-7: Workflow preservation

File-plus-position symbol resolution continues to work unchanged for existing agent workflows.

## Release Priorities

### Priority 1

- qualified-name resolution;
- ambiguity handling;
- readable symbol handles.

### Priority 2

- reliable implementation and override discovery.

### Priority 3

- explicit uncertainty modeling in call-flow and related outputs.

### Priority 4

- code-smell prioritization, grouping, and agent-safe filtering.

### Priority 5

- batch workflow capabilities.

## Handoff Criteria

Architect phase is complete when:

- requirements are agreed and stable;
- architecture boundaries and output-model changes are specified;
- public contract changes are identified clearly enough for implementation;
- acceptance criteria are testable.

Architect phase complete -> handing over to code-monkey for implementation.
