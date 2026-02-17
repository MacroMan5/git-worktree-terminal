using System.Diagnostics;
using System.IO;
using tmuxlike.Models;

namespace tmuxlike.Services;

/// <summary>
/// Provides static methods for interacting with git repositories and worktrees.
/// </summary>
public class GitService
{
    /// <summary>
    /// Checks whether the given path is inside a git repository.
    /// </summary>
    /// <param name="path">The filesystem path to check.</param>
    /// <returns><c>true</c> if the path is inside a git work tree; otherwise <c>false</c>.</returns>
    public static bool IsGitRepository(string path)
    {
        var result = RunGit(path, "rev-parse --is-inside-work-tree");
        return result.ExitCode == 0 && result.Output.Trim() == "true";
    }

    /// <summary>
    /// Returns the root directory of the git repository containing the given path.
    /// </summary>
    /// <param name="path">A path inside the repository.</param>
    /// <returns>The absolute path to the repository root.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the path is not inside a git repository.</exception>
    public static string GetRepoRoot(string path)
    {
        var result = RunGit(path, "rev-parse --show-toplevel");
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Not a git repository: {path}");
        return result.Output.Trim().Replace('/', '\\');
    }

    /// <summary>
    /// Lists all worktrees for the repository at the given path.
    /// </summary>
    /// <param name="repoPath">The path to the repository or any of its worktrees.</param>
    /// <returns>A list of <see cref="WorktreeInfo"/> objects, or an empty list on failure.</returns>
    public static List<WorktreeInfo> GetWorktrees(string repoPath)
    {
        var result = RunGit(repoPath, "worktree list --porcelain");
        if (result.ExitCode != 0)
            return [];

        var worktrees = new List<WorktreeInfo>();
        WorktreeInfo? current = null;
        bool isFirst = true;

        foreach (var line in result.Output.Split('\n', StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                if (current != null)
                {
                    current.IsMain = isFirst;
                    worktrees.Add(current);
                    current = null;
                    isFirst = false;
                }
                continue;
            }

            if (trimmed.StartsWith("worktree "))
            {
                current = new WorktreeInfo { Path = trimmed[9..].Replace('/', '\\') };
            }
            else if (trimmed.StartsWith("HEAD ") && current != null)
            {
                current.HeadCommit = trimmed[5..];
                if (current.HeadCommit.Length > 7)
                    current.HeadCommit = current.HeadCommit[..7];
            }
            else if (trimmed.StartsWith("branch ") && current != null)
            {
                var branch = trimmed[7..];
                if (branch.StartsWith("refs/heads/"))
                    branch = branch[11..];
                current.Branch = branch;
            }
        }

        if (current != null)
        {
            current.IsMain = isFirst;
            worktrees.Add(current);
        }

        return worktrees;
    }

    /// <summary>
    /// Creates a new worktree with a new branch at the specified path.
    /// </summary>
    /// <param name="repoPath">The repository root path.</param>
    /// <param name="branchName">The name of the new branch to create.</param>
    /// <param name="worktreePath">The filesystem path where the worktree will be created.</param>
    /// <returns>A tuple indicating success and a descriptive message.</returns>
    public static (bool Success, string Message) AddWorktree(string repoPath, string branchName, string worktreePath)
    {
        var result = RunGit(repoPath, $"worktree add \"{worktreePath}\" -b \"{branchName}\"");
        return result.ExitCode == 0
            ? (true, "Worktree created successfully.")
            : (false, result.Error.Trim());
    }

    /// <summary>
    /// Removes an existing worktree and deletes its directory.
    /// </summary>
    /// <param name="repoPath">The repository root path.</param>
    /// <param name="worktreePath">The filesystem path of the worktree to remove.</param>
    /// <returns>A tuple indicating success and a descriptive message.</returns>
    public static (bool Success, string Message) RemoveWorktree(string repoPath, string worktreePath)
    {
        var result = RunGit(repoPath, $"worktree remove \"{worktreePath}\"");
        return result.ExitCode == 0
            ? (true, "Worktree removed successfully.")
            : (false, result.Error.Trim());
    }

    private static (int ExitCode, string Output, string Error) RunGit(string workingDir, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (-1, "", "Failed to start git process");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);
            return (process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
