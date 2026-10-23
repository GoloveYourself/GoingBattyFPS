namespace GoingBatty
{
    /// <summary>Where the game looks for updates. Must match the GitHub repository.</summary>
    public static class BuildInfo
    {
        public const string RepoOwner = "GoloveYourself";
        public const string RepoName = "GoingBattyFPS";
        public static string ReleasesPage => $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";
    }
}
