#tool "nuget:https://api.nuget.org/v3/index.json?package=KuduSync.NET&version=1.4.0"
#tool "nuget:https://api.nuget.org/v3/index.json?package=Wyam&version=1.4.1"
#addin "nuget:https://api.nuget.org/v3/index.json?package=Cake.Wyam&version=1.4.1"
#addin "nuget:https://api.nuget.org/v3/index.json?package=Cake.Git&version=0.18.0"
#addin "nuget:https://api.nuget.org/v3/index.json?package=Cake.Kudu&version=0.8.0"


//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");


// Define variables
var isRunningOnAppVeyor = AppVeyor.IsRunningOnAppVeyor;
var isPullRequest       = AppVeyor.Environment.PullRequest.IsPullRequest;
var accessToken         = EnvironmentVariable("git_access_token");
var deployRemote        = EnvironmentVariable("git_deploy_remote");
var currentBranch       = isRunningOnAppVeyor ? BuildSystem.AppVeyor.Environment.Repository.Branch : GitBranchCurrent("./").FriendlyName;
var deployBranch        = "master";

// Define directories.
var releaseDir          = Directory("./release");
var outputPath          = MakeAbsolute(Directory("./output"));
var rootPublishFolder   = MakeAbsolute(Directory("publish"));


//////////////////////////////////////////////////////////////////////
// SETUP
//////////////////////////////////////////////////////////////////////

Setup(ctx =>
{
    // Executed BEFORE the first task.
    Information("Building branch {0} ({1})...", currentBranch, deployBranch);
});


//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    EnsureDirectoryExists(releaseDir);
});


Task("Build")
    .IsDependentOn("Clean")
    .Does(() =>
{
    Wyam(new WyamSettings
    {
        Recipe = "Docs",
        Theme = "Samson",
        UpdatePackages = true
    });
});


Task("Preview")
    .Does(() =>
{
    Wyam(new WyamSettings
    {
        Recipe = "Docs",
        Theme = "Samson",
        UpdatePackages = true,
        Preview = true
    });
});

Task("Copy-Repo-Files")
    .IsDependentOn("Build")
    .Does(() =>
{
    CopyFiles(
        new FilePath[] {
            //"CNAME", add when domain in place
            "LICENSE",
            "README.md",
            "appveyor.yml"
        },
        "./output"
        );
});


Task("Deploy")
    .WithCriteria(isRunningOnAppVeyor)
    .WithCriteria(!isPullRequest)
    .WithCriteria(!string.IsNullOrEmpty(accessToken))
    .WithCriteria(currentBranch == "develop")
    .WithCriteria(!string.IsNullOrEmpty(deployRemote))
    .WithCriteria(!string.IsNullOrEmpty(deployBranch))
    .IsDependentOn("Copy-Repo-Files")
    .Does(() =>
    {
        EnsureDirectoryExists(rootPublishFolder);
        var sourceCommit = GitLogTip("./");
        var publishFolder = rootPublishFolder.Combine(DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Information("Getting publish branch {0}...", deployBranch);
        GitClone(deployRemote, publishFolder, new GitCloneSettings{ BranchName = deployBranch });

        Information("Sync output files...");
        Kudu.Sync(outputPath, publishFolder, new KuduSyncSettings {
            PathsToIgnore = new []{ ".git", ".gitignore" }
        });

        Information("Stage all changes...");
        GitAddAll(publishFolder);

        Information("Commit all changes...");
        GitCommit(
            publishFolder,
            sourceCommit.Committer.Name,
            sourceCommit.Committer.Email,
            string.Format("AppVeyor Publish: {0}\r\n{1}", sourceCommit.Sha, sourceCommit.Message)
            );

        Information("Pushing all changes...");
        GitPush(publishFolder, accessToken, "x-oauth-basic", deployBranch);
    });


Task("Default")
    .IsDependentOn("Build");

Task("AppVeyor")
    .IsDependentOn("Deploy");

RunTarget(target);