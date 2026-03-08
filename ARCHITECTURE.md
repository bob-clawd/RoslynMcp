# Architecture: code_inspection Reliability, Uncertainty, and Agent Ergonomics

## Intent

This architecture defines the system-level shape for improving `code_inspection` based on the issues captured in `docs/TOOL_AUDIT.md`.

The design goal is not to make static analysis magically know runtime truth. The goal is to make the toolset more reliable, more explicit about uncertainty, and more ergonomic for agent-driven workflows.

## Architectural Principles

### 1. Separate certainty from inference

Direct static facts, inferred relationships, and known blind spots must not be mixed into one undifferentiated result shape.

### 2. Preserve strong paths

Existing position-based workflows are already a strength and must remain stable.

### 3. Share semantics across tools

Tools that talk about symbols, implementations, or call relationships must rely on compatible internal models so the system does not produce semantically drifted answers.

### 4. Improve ergonomics without sacrificing precision

Human-readable or shorter handles may be added, but precision-grade identifiers and machine-stable representations remain available.

### 5. Prioritize truthfulness over false confidence

Where the analysis cannot know enough, the system should return constrained, explicit uncertainty rather than overclaim.

## Scope

This architecture covers:

- symbol addressing and symbol references;
- implementation and override discovery;
- call-flow uncertainty modeling;
- code-smell prioritization and grouping;
- batch workflow surfaces.

It does not define business logic for specific tool handlers or prescribe concrete Roslyn API usage.

## Capability Map

The system should be organized around five capability layers.

### A. Symbol Addressing Layer

#### Responsibility

Translate human-readable symbol inputs into stable internal symbol identity.

#### Required capabilities

- parse qualified type names;
- parse fully qualified member names;
- normalize readable method signatures for overload selection;
- resolve generics in a readable input model;
- return structured ambiguity candidates when unique resolution is not possible.

#### Public impact

Primarily affects:

- `resolve_symbol`;
- any future batch symbol-resolution endpoint;
- response payloads that expose reusable symbol references.

#### Key rule

Name-based addressing augments position-based addressing. It does not replace it.

### B. Symbol Reference Model

#### Responsibility

Define how a symbol is represented across tool boundaries.

#### Required capabilities

- retain raw `symbolId` for full-fidelity identity;
- expose a shorter or more readable handle for chaining;
- expose a normalized display form that is understandable to humans;
- ensure round-trip support for follow-up tool calls where feasible.

#### Rationale

The current `symbolId` works for machines but is poor as an interactive contract surface. The architecture must introduce a second, more ergonomic reference layer without weakening correctness.

### C. Semantic Relationship Layer

#### Responsibility

Provide shared semantics for hierarchy, implementations, overrides, callers, and call-flow relationships.

#### Required capabilities

- unified treatment of interface implementation;
- unified treatment of abstract member implementation;
- unified treatment of virtual override discovery;
- consistent distinction between direct static relationships and inferred or potential targets.

#### Public impact

Primarily affects:

- `find_implementations`;
- `find_callers`;
- `trace_call_flow`;
- hierarchy-related tools when member relationships are involved.

#### Architectural constraint

No tool may silently invent a stronger relationship type than the underlying semantic evidence supports.

### D. Uncertainty Modeling Layer

#### Responsibility

Represent where and why static analysis is incomplete.

#### Required capabilities

- classify uncertainty reasons;
- attach uncertainty to edges or result entries;
- distinguish confirmed direct targets from possible targets;
- expose blind spots in a structured, machine-readable way.

#### Proposed uncertainty categories

- `interface_dispatch`
- `polymorphic_inference`
- `reflection_blindspot`
- `dynamic_unresolved`
- `unresolved_project`
- `project_inference_degraded`

#### Public impact

Primarily affects:

- `trace_call_flow`;
- potentially `find_callers` and `find_implementations` where inferred relationships are exposed.

#### Key rule

Uncertainty must be additive metadata, not hidden interpretation that only a human can infer.

### E. Finding Prioritization Layer

#### Responsibility

Transform raw analysis findings into review-relevant, agent-usable output.

#### Required capabilities

- normalize risk and category presentation;
- separate style/trivia findings from higher-value semantic findings;
- group related findings by code zone, symbol, or location cluster;
- support a conservative filter mode for agent-oriented review.

#### Public impact

Primarily affects:

- `find_codesmells`.

#### Key rule

The system must not present a wall of minor trivia as if it were the most important review outcome.

### F. Batch Workflow Layer

#### Responsibility

Reduce round-trips for common multi-step agent workflows.

#### Required capabilities

- batch symbol resolution;
- batch formatting;
- multi-symbol usage/caller aggregation;
- preview-style mutation or impact operations where appropriate.

#### Public impact

May introduce new tool contracts rather than overload existing single-item tools.

#### Constraint

Batch support must compose existing semantic models rather than reimplement them separately.

## Tool Contract Strategy

### Contract principle 1: additive evolution first

Where possible, extend result payloads additively so existing consumers continue to function.

### Contract principle 2: structured over textual

Ambiguity, confidence, uncertainty, grouping, and prioritization must be represented as structured fields rather than prose-only descriptions.

### Contract principle 3: evidence-bearing outputs

If a result is inferred or incomplete, the contract should expose enough metadata for an agent to decide whether to trust, verify, or escalate to file reading or runtime validation.

## Proposed Output Concepts

The exact schema is implementation-owned, but the architecture requires support for concepts equivalent to the following.

### Symbol result concepts

- canonical machine identifier;
- readable handle;
- normalized display signature;
- declaration location;
- ambiguity candidate list when unique match is absent.

### Relationship result concepts

- relationship kind;
- confidence or evidence mode;
- direct versus possible target distinction;
- uncertainty tags;
- source and target symbol references.

### Finding result concepts

- normalized category;
- normalized priority or risk;
- grouped zone or cluster identity;
- optional review mode filtering;
- distinction between code-fix hint and higher-order review concern.

## Module Boundaries

The following architectural boundaries should remain explicit.

### Boundary 1: Parsing vs semantic resolution

Readable input parsing is a separate concern from semantic lookup. The parser normalizes intent; the semantic resolver validates and binds it against the solution.

### Boundary 2: Core semantic facts vs presentation

Symbol identity, relationship identity, and uncertainty classification belong to shared semantic models. Human-readable displays and summaries are presentation concerns layered on top.

### Boundary 3: Raw findings vs prioritized review view

Analyzer discovery and finding prioritization are not the same responsibility. Raw findings may remain available, but prioritized agent-facing views must be explicitly derived.

### Boundary 4: Single-item tools vs batch orchestration

Batch workflows should reuse the same semantic engines as single-item tools. They should not fork behavior or invent new semantics.

## Risk Areas

### Risk 1: False confidence in inferred results

If possible targets and direct static targets are not kept distinct, agents may over-trust the output.

### Risk 2: Semantic drift across tools

If `find_implementations`, `trace_call_flow`, and caller analysis use different internal logic, contradictions will undermine trust.

### Risk 3: Ergonomic handle instability

If a shorter handle is introduced without clear stability rules, it may become more confusing than helpful.

### Risk 4: Smell reprioritization hiding useful raw data

The architecture should support both filtered and fuller result views so signal quality improves without losing access to raw detail when needed.

### Risk 5: Batch APIs becoming a dumping ground

Batch operations should package existing capabilities, not create a second inconsistent API universe.

## Recommended Delivery Order

### Phase 1: Symbol addressing foundation

Deliver:

- qualified-name parsing and resolution;
- ambiguity result model;
- readable symbol handle support.

Why first:

- it improves nearly every other agent workflow;
- it creates the identity contract needed by later enhancements.

### Phase 2: Polymorphism correctness

Deliver:

- reliable interface/abstract/virtual implementation discovery;
- shared member-relationship semantics.

Why second:

- it fixes a correctness gap, not just ergonomics.

### Phase 3: Uncertainty-aware flow analysis

Deliver:

- uncertainty categories;
- possible-target representation;
- explicit blind-spot signaling in flow outputs.

Why third:

- it improves trust calibration without pretending to solve runtime omniscience.

### Phase 4: Review-signal quality

Deliver:

- grouped findings;
- stronger prioritization;
- agent-safe mode.

Why fourth:

- it is valuable, but less foundational than symbol identity and polymorphism correctness.

### Phase 5: Batch workflow optimization

Deliver:

- selected multi-item operations for common agent paths.

Why fifth:

- batch capability should sit on top of stable semantics, not arrive before them.

## Validation Strategy

The implementation handoff must validate architecture with scenario-based tests, not only isolated unit behavior.

Required validation themes:

- qualified-name resolution across types, generics, and overloads;
- ambiguous-name disambiguation behavior;
- override and implementation discovery across inheritance and interfaces;
- explicit uncertainty on interface and reflection-adjacent call-flow scenarios;
- grouped and prioritized smell output in mixed-quality files;
- compatibility of new outputs with existing file-plus-position workflows.

## Handoff to code-monkey

Implementation scope should be split into discrete work packages:

1. public contract extensions for symbol resolution and symbol references;
2. shared semantic relationship improvements for implementations and overrides;
3. uncertainty metadata in call-flow outputs;
4. smell prioritization and grouped review modes;
5. optional batch tool surfaces after the core semantics are stable.

Constraints for implementation:

- do not weaken existing position-based workflows;
- do not blur direct facts and inferred possibilities;
- do not introduce tool-specific semantic drift;
- do not frame static approximations as runtime truth.

Definition of done for implementation handoff:

- public contracts are updated consistently;
- acceptance criteria from `REQUIREMENTS.md` are testable and satisfied;
- output semantics are aligned across affected tools;
- documentation explains both strengths and explicit limits.

Architect phase complete -> handing over to code-monkey for implementation.
