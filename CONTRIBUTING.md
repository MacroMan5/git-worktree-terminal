# Contributing to Git Worktree Terminal

Contributions are welcome! Here's how to get started.

## Development Setup

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 version 1809 or later
- Git
- Visual Studio 2022 or VS Code with C# Dev Kit

### Building

```bash
git clone https://github.com/MacroMan/git-worktree-terminal.git
cd git-worktree-terminal
dotnet build tmuxlike/tmuxlike.csproj
```

### Running

Run from inside any git repository:

```bash
dotnet run --project tmuxlike/tmuxlike.csproj
```

## Workflow

1. Fork the repository
2. Create a feature branch from `main`: `git checkout -b feature/my-feature`
3. Make your changes
4. Test by building and running the app in a git repository
5. Commit with a clear message describing the change
6. Push to your fork and open a pull request

## Pull Request Guidelines

- Keep PRs focused — one feature or fix per PR
- Update `CHANGELOG.md` under an `[Unreleased]` section
- Ensure the project builds without warnings: `dotnet build tmuxlike/tmuxlike.csproj`
- Describe what the change does and why in the PR description

## Code Style

- Follow standard C# conventions
- Use file-scoped namespaces
- Use `var` where the type is obvious
- Keep methods short and focused
- Add XML documentation comments (`/// <summary>`) to public types and members

## Reporting Issues

- Use [GitHub Issues](https://github.com/MacroMan/git-worktree-terminal/issues)
- Include your Windows version and .NET SDK version
- Describe what you expected vs. what happened
- Include steps to reproduce if possible
