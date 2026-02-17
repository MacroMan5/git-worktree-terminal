# Git Worktree Terminal

[![Release](https://img.shields.io/github/v/release/MacroMan/git-worktree-terminal)](https://github.com/MacroMan/git-worktree-terminal/releases)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Platform-Windows-0078D4.svg)](https://github.com/MacroMan/git-worktree-terminal/releases)

A tmux-inspired terminal manager for git worktrees on Windows. Switch between worktrees, split terminal panes, and browse files — all from a single window.

![Git Worktree Terminal](https://img.shields.io/badge/Status-Stable-brightgreen)

## Why?

If you work with **multiple parallel worktrees** — running AI agents across branches, reviewing PRs while developing on another branch, or just keeping multiple features in flight — switching between separate terminal windows and VS Code instances gets painful fast.

Git Worktree Terminal gives you a **single window** where every worktree has its own terminal session (with tmux-style split panes), a file explorer, and one-click VS Code integration. Click a worktree in the sidebar, and you're immediately in that branch's directory with a live terminal.

## Features

- **Worktree sidebar** — Lists all git worktrees with branch names and paths. Click to switch.
- **Split terminal panes** — Up to 4 panes per worktree in a tiled grid layout (like tmux).
- **Embedded terminal** — Full ConPTY-based terminal with PowerShell, pwsh, or cmd.
- **File explorer** — Toggleable tree view with lazy-loaded directories. Double-click to open in VS Code.
- **VS Code integration** — Open the current worktree folder or individual files directly in VS Code.
- **Worktree management** — Create new worktrees (auto-generates branch + path) and delete existing ones.
- **Session persistence** — Terminal sessions are preserved when switching between worktrees.
- **Dark theme** — VS Code-inspired dark UI throughout.

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+N` | Create new worktree |
| `F5` | Refresh worktree list |
| `Delete` | Remove selected worktree |
| `Ctrl+E` | Toggle file explorer |
| `Ctrl+O` | Open worktree in VS Code |
| `Ctrl+T` | Split terminal pane |
| `Ctrl+W` | Close focused pane |
| `Escape` | Return focus from file explorer to terminal |

## Installation

### Download (recommended)

Download `git-worktree-terminal.exe` from the [latest release](https://github.com/MacroMan/git-worktree-terminal/releases/latest). It's a single self-contained executable — no .NET runtime required.

### Build from source

```bash
git clone https://github.com/MacroMan/git-worktree-terminal.git
cd git-worktree-terminal
dotnet publish tmuxlike/tmuxlike.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The executable will be at `tmuxlike/bin/Release/net8.0-windows7.0/win-x64/publish/tmuxlike.exe`.

## Usage

1. Open a terminal in any git repository
2. Run `git-worktree-terminal.exe` (or `tmuxlike.exe` if built from source)
3. The app detects the repo root and lists all worktrees
4. Click a worktree to open a terminal session in that directory
5. Use `Ctrl+T` to split into multiple panes

## Requirements

- Windows 10 version 1809 or later
- `git` on your PATH
- `code` on your PATH (optional, for VS Code integration)

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
