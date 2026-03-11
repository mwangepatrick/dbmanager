# Project Guidelines

## Code Style
- This is a C# console application in a single main file: DbManager/Program.cs.
- Keep changes small and behavior-preserving unless explicitly asked to refactor.
- Preserve existing naming and menu-driven CLI flow.
- Prefer clear user-facing console messages for each operation step and failure.

## Architecture
- Core app entry and workflows live in DbManager/Program.cs.
- Main responsibilities:
  - Database config read/write and encryption helpers
  - Interactive menu loop for DB operations
  - PostgreSQL operations through Npgsql
  - Backup/restore and psql shell integration via ProcessStartInfo
- Keep PostgreSQL operation logic grouped and avoid spreading cross-cutting concerns into unrelated methods.

## Build And Run
- Build from repo root: dotnet build DbManager/DbManager.csproj -c Debug
- Run from repo root: dotnet run --project DbManager/DbManager.csproj
- Build from project folder: dotnet build DbManager.csproj
- Run from project folder: dotnet run
- This project requires PostgreSQL CLI tools in PATH for some features:
  - psql
  - pg_dump
  - pg_restore

## Conventions
- Use Npgsql APIs and parameterized SQL for user-provided values.
- Prefer ProcessStartInfo with explicit arguments; avoid shell command string concatenation when possible.
- Validate interactive inputs before destructive operations (drop/rename/import).
- Keep compatibility with Windows PowerShell usage and current operator flow.
- Do not introduce broad restructuring unless requested; prioritize targeted fixes.

## Pitfalls
- Legacy files may still exist from pre-SDK migration (for example App.config, packages.config); source of truth is DbManager/DbManager.csproj.
- The app depends on external PostgreSQL executables; when fixing runtime issues, check PATH/tool availability first.
