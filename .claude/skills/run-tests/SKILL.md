---
name: run-tests
description: Run the DigiSandra test suite
argument-hint: "[filter]"
allowed-tools: Bash(dotnet *)
---

Run the test suite for DigiSandra.

```bash
# All tests
dotnet test

# With coverage
dotnet test --collect:"XPlat Code Coverage"

# Specific filter (if $ARGUMENTS provided)
dotnet test --filter "FullyQualifiedName~$ARGUMENTS"
```

If `$ARGUMENTS` is empty, run all tests. Report results clearly: number passed, failed, and skipped. If failures occur, show the failing test names and error messages.
