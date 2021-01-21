#addin "nuget:?package=Cake.Git&version=0.22.0"

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var configuration = Argument("configuration", "Debug");
var revision = EnvironmentVariable("BUILD_NUMBER") ?? Argument("revision", "9999");
var target = Argument("target", "Default");


//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// Define git commit id
var commitId = "SNAPSHOT";

// Define product name and version
var product = "VitaDX9";
var companyName = "HTC";
var version = "1.3.0";
var semanticVersion = $"{version}.{revision}";
var ciVersion = $"{version}.0";
var buildVersion = "Release".Equals(configuration) ? semanticVersion : $"{ciVersion}-CI{revision}";
var nugetTags = new [] {"htc", "vita", "dx9"};
var projectUrl = "https://github.com/ViveportSoftware/vita_external_dx9_setup";
var description = "HTC Package for DirectX 9.0";

// Define copyright
var copyright = $"Copyright © 2021 - {DateTime.Now.Year}";

// Define timestamp for signing
var lastSignTimestamp = DateTime.Now;
var signIntervalInMilli = 1000 * 5;

// Define path
var solutionFile = File($"./source/{product}.sln");
var wixInsigniaFile = File(EnvironmentVariable("WIX") + "bin/insignia.exe");

// Define directories.
var distDir = Directory("./dist");
var tempDir = Directory("./temp");
var packagesDir = Directory("./source/packages");
var nugetDir = distDir + Directory(configuration) + Directory("nuget");

// Define signing key, password and timestamp server
var signKeyEnc = EnvironmentVariable("SIGNKEYENC") ?? "NOTSET";
var signPass = EnvironmentVariable("SIGNPASS") ?? "NOTSET";
var signSha1Uri = new Uri("http://timestamp.digicert.com");
var signSha256Uri = new Uri("http://timestamp.digicert.com");

// Define nuget push source and key
var nugetApiKey = EnvironmentVariable("NUGET_PUSH_TOKEN") ?? EnvironmentVariable("NUGET_APIKEY") ?? "NOTSET";
var nugetSource = EnvironmentVariable("NUGET_PUSH_PATH") ?? EnvironmentVariable("NUGET_SOURCE") ?? "NOTSET";


//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Fetch-Git-Commit-ID")
    .ContinueOnError()
    .Does(() =>
{
    var lastCommit = GitLogTip(MakeAbsolute(Directory(".")));
    commitId = lastCommit.Sha;
});

Task("Display-Config")
    .IsDependentOn("Fetch-Git-Commit-ID")
    .Does(() =>
{
    Information($"Build target:        {target}");
    Information($"Build configuration: {configuration}");
    Information($"Build commitId:      {commitId}");
    Information($"Build version:       {buildVersion}");
    Information($"WiX Insignia path:   {wixInsigniaFile}");
});

Task("Clean-Workspace")
    .IsDependentOn("Display-Config")
    .Does(() =>
{
    CleanDirectory(distDir);
    CleanDirectory(tempDir);
    CleanDirectory(packagesDir);
});

Task("Restore-NuGet-Packages")
    .IsDependentOn("Clean-Workspace")
    .Does(() =>
{
    NuGetRestore(new FilePath($"./source/{product}.sln"));
});

Task("Build-WiX-Package")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{
    // Use MSBuild
    MSBuild(
            solutionFile,
            settings => settings.SetConfiguration(configuration)
                                .WithProperty("MyPackageVersion", semanticVersion)
    );
});

Task("Sign-WiX-Package")
    .WithCriteria(() => "Release".Equals(configuration) && !"NOTSET".Equals(signPass) && !"NOTSET".Equals(signKeyEnc))
    .IsDependentOn("Build-WiX-Package")
    .Does(() =>
{
    var currentSignTimestamp = DateTime.Now;
    Information($"Last timestamp:    {lastSignTimestamp}");
    Information($"Current timestamp: {currentSignTimestamp}");
    var totalTimeInMilli = (DateTime.Now - lastSignTimestamp).TotalMilliseconds;

    var signKey = "./temp/key.pfx";
    System.IO.File.WriteAllBytes(signKey, Convert.FromBase64String(signKeyEnc));

    var file = $"./temp/{configuration}/{product}.Package.x86/bin/x86/{product}.Package.x86.msi";

    if (totalTimeInMilli < signIntervalInMilli)
    {
        System.Threading.Thread.Sleep(signIntervalInMilli - (int)totalTimeInMilli);
    }
    Sign(
            file,
            new SignToolSignSettings
            {
                    TimeStampUri = signSha1Uri,
                    CertPath = signKey,
                    Password = signPass
            }
    );
    lastSignTimestamp = DateTime.Now;
});

Task("Build-WiX-Bundle")
    .IsDependentOn("Sign-WiX-Package")
    .Does(() =>
{
    // Use MSBuild
    MSBuild(
            solutionFile,
            settings => settings.SetConfiguration(configuration)
                                .WithProperty("MyPackageVersion", semanticVersion)
    );
});

Task("Sign-WiX-Bundle")
    .WithCriteria(() => "Release".Equals(configuration) && !"NOTSET".Equals(signPass) && !"NOTSET".Equals(signKeyEnc))
    .IsDependentOn("Build-WiX-Bundle")
    .Does(() =>
{
    var currentSignTimestamp = DateTime.Now;
    Information($"Last timestamp:    {lastSignTimestamp}");
    Information($"Current timestamp: {currentSignTimestamp}");
    var totalTimeInMilli = (DateTime.Now - lastSignTimestamp).TotalMilliseconds;

    var signKey = "./temp/key.pfx";
    System.IO.File.WriteAllBytes(signKey, Convert.FromBase64String(signKeyEnc));

    var file = $"./temp/{configuration}/{product}Setup/bin/x86/{product}Setup.exe";
    var engileFile = $"./temp/{configuration}/{product}Setup/bin/x86/{product}Setup.engine.exe";

    if (totalTimeInMilli < signIntervalInMilli)
    {
        System.Threading.Thread.Sleep(signIntervalInMilli - (int)totalTimeInMilli);
    }
    var insigniaProcessArgs = new ProcessArgumentBuilder()
            .Append("-ib")
            .Append(file)
            .Append("-o")
            .Append(engileFile);
    using(var insigniaProcess = StartAndReturnProcess(
            wixInsigniaFile,
            new ProcessSettings
            {
                    Arguments = insigniaProcessArgs
            }
    ))
    {
        insigniaProcess.WaitForExit();
    }
    Sign(
            engileFile,
            new SignToolSignSettings
            {
                    TimeStampUri = signSha1Uri,
                    CertPath = signKey,
                    Password = signPass
            }
    );
    lastSignTimestamp = DateTime.Now;

    System.Threading.Thread.Sleep(signIntervalInMilli);
    Sign(
            engileFile,
            new SignToolSignSettings
            {
                    AppendSignature = true,
                    TimeStampUri = signSha256Uri,
                    DigestAlgorithm = SignToolDigestAlgorithm.Sha256,
                    TimeStampDigestAlgorithm = SignToolDigestAlgorithm.Sha256,
                    CertPath = signKey,
                    Password = signPass
            }
    );
    lastSignTimestamp = DateTime.Now;

    insigniaProcessArgs = new ProcessArgumentBuilder()
            .Append("-ab")
            .Append(engileFile)
            .Append(file)
            .Append("-o")
            .Append(file);
    using(var insigniaProcess = StartAndReturnProcess(
            wixInsigniaFile,
            new ProcessSettings
            {
                    Arguments = insigniaProcessArgs
            }
    ))
    {
        insigniaProcess.WaitForExit();
    }
    Sign(
            file,
            new SignToolSignSettings
            {
                    TimeStampUri = signSha1Uri,
                    CertPath = signKey,
                    Password = signPass
            }
    );
    lastSignTimestamp = DateTime.Now;

    System.Threading.Thread.Sleep(signIntervalInMilli);
    Sign(
            file,
            new SignToolSignSettings
            {
                    AppendSignature = true,
                    TimeStampUri = signSha256Uri,
                    DigestAlgorithm = SignToolDigestAlgorithm.Sha256,
                    TimeStampDigestAlgorithm = SignToolDigestAlgorithm.Sha256,
                    CertPath = signKey,
                    Password = signPass
            }
    );
    lastSignTimestamp = DateTime.Now;
});

Task("Build-NuGet-Package")
    .IsDependentOn("Sign-WiX-Bundle")
    .Does(() =>
{
    CreateDirectory(nugetDir);
    var nuGetPackSettings = new NuGetPackSettings
    {
            Id = product,
            Version = buildVersion,
            Authors = new[] {"HTC"},
            Description = $"{description} [CommitId: {commitId}]",
            Copyright = copyright,
            ProjectUrl = new Uri(projectUrl),
            Tags = nugetTags,
            RequireLicenseAcceptance= false,
            Files = new []
            {
                    new NuSpecContent
                    {
                            Source = $"{product}Setup/bin/x86/{product}Setup.exe",
                            Target = "content"
                    }
            },
            Properties = new Dictionary<string, string>
            {
                    {"Configuration", configuration}
            },
            BasePath = tempDir + Directory(configuration),
            OutputDirectory = nugetDir
    };

    NuGetPack(nuGetPackSettings);

    CopyFile(
            $"./temp/{configuration}/{product}Setup/bin/x86/{product}Setup.exe",
            $"./dist/{configuration}/{product}Setup-{buildVersion}.exe"
    );
});

Task("Publish-NuGet-Package")
    .WithCriteria(() => "Release".Equals(configuration) && !"NOTSET".Equals(nugetApiKey) && !"NOTSET".Equals(nugetSource))
    .IsDependentOn("Build-NuGet-Package")
    .Does(() =>
{
    NuGetPush(
            new FilePath($"./dist/{configuration}/nuget/{product}.{buildVersion}.nupkg"),
            new NuGetPushSettings
            {
                    Source = nugetSource,
                    ApiKey = nugetApiKey
            }
    );
});


//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Build-NuGet-Package");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
