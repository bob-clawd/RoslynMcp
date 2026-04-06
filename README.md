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

## Recommended Workflows

Choose the entry point based on what you already know.

```mermaid
flowchart TD
    ls[load_solution] --> lp[load_project]
    lp --> lt[load_type]
    lt --> lm[load_member]

    ls --> st[search_type]
    ls --> sm[search_member]

    st --> lt
    sm --> lm

    lm --> cd[check_document]
    cd --> rt[run_tests]
```

### Typical paths

- **I know the project**  
  `load_solution` → `load_project` → `load_type` → `load_member`

- **I only know a type name**  
  `load_solution` → `search_type`

- **I only know a member name**  
  `load_solution` → `search_member`

- **I changed a file and want quick validation**  
  `load_solution` → `check_document`

- **I want broader verification**  
  `run_tests`

This keeps navigation semantic and symbol-aware without relying on text-only search, while still giving you fast search and file-level validation when you need it.

The full tool descriptions can be [found here](https://github.com/chrismo80/RoslynMcp/blob/main/TOOLS.md)
