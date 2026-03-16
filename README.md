![plot](assets/icon.png)



# RoslynMcp

A Model Context Protocol (MCP) server that brings Roslyn code intelligence to AI agents.


## Get It on NuGet

[![NuGet](https://img.shields.io/nuget/v/RoslynMcp.svg)](https://www.nuget.org/packages/RoslynMcp/)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://www.nuget.org/packages/RoslynMcp/)

_This project uses Roslynator, licensed under Apache 2.0._

#### Installation

```bash
dotnet tool install -g RoslynMcp
```

#### Update

```bash
dotnet tool update -g RoslynMcp
```


#### MCP config (OpenCode)

```json
  "mcp": {
    "roslyn": {
      "type": "local",
      "command": [
        "roslynmcp"
      ]
    }
  }
```


## What It Is

RoslynMcp is a .NET application that exposes the power of [Roslyn](https://github.com/dotnet/roslyn) (the .NET compiler platform) through the MCP protocol. It acts as a bridge between AI assistants and your C# codebase, enabling deep code understanding and analysis.

## Why It Exists

Traditional AI code assistants often rely on simplistic pattern matching (grep/glob) which misses semantic context. RoslynMcp solves this by providing:

- **Semantic understanding** — It knows what your code *means*, not just what it *says*
- **Symbol resolution** — Understands types, methods, properties across your entire solution
- **Call graph tracing** — See how code flows through your system
- **Code smell detection** — Identifies potential issues using [Roslynator](https://github.com/dotnet/roslynator) analyzers

## What You Can Use It For

| Inspection Tool          | Description                                                        |
| ------------------------ | ------------------------------------------------------------------ |
| **Load Solution**        | Loads a .sln/.slnx file and prepare the workspace                  |
| **Understand Projects**  | Explore project relationships, types, and deep-profile hotspots    |
| **List Types**           | Discover all classes, interfaces, enums in a project               |
| **List Members**         | Explore methods, properties, fields of any type                    |
| **Resolve Symbol**       | Get canonical IDs for a single code symbol                         |
| **Resolve Symbols**      | Resolve multiple symbols in one round-trip                         |
| **Explain Symbol**       | Understand what a symbol does and where it's used                  |
| **Trace Call Flow**      | See upstream callers or downstream callees                         |
| **Find Callers**         | Return only immediate direct upstream callers                      |
| **Find Callees**         | Return only immediate direct downstream callees                    |
| **Find Usages**          | Locate all references to a type/member                             |
| **Find Implementations** | Locate all implementations of a interface or abstract class/method |
| **Get Type Hierarchy**   | Explore type inheritance and derived types                         |
| **Find Code Smells**     | Detect potential issues in a file                                  |

| Mutation Tool              | Description                                                                    |
| -------------------------- | ------------------------------------------------------------------------------ |
| **Add Method**             | Add a helper or overload to an existing type using symbol-aware insertion      |
| **Replace Method**         | Replace a full method declaration when name, signature, or modifiers must move |
| **Replace Method Body**    | Change only method logic while preserving the existing declaration shape        |
| **Delete Method**          | Remove an obsolete method or disposable helper by exact symbol target          |
| **Rename Symbol**          | Rename operation for types, methods, etc.                                      |
| **Format Document**        | Format a C# source file using the solution's settings                          |

### When To Use Mutation Tools vs Text Edits

Use mutation tools when an agent needs a precise, symbol-aware edit and wants Roslyn to target the exact declaration instead of relying on file positions or text matching. They are especially useful for overloaded methods, signature changes, targeted logic replacement, and cleanup work where touching the wrong method would be expensive.

Prefer normal text edits when the task spans multiple nearby code regions, when the desired syntax is not well expressed by the mutation API, or when the agent is still shaping code quickly and does not yet need symbol-level precision.

In practice, the best workflow is often hybrid: resolve the symbol with Roslyn, use mutation tools for the risky surgical edit, and fall back to normal file editing for broader reshaping around it.


