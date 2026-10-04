#if UNITY_ANDROID
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Reconciles the EOS plugin's Android setup with the Gradle toolchain this Unity
/// ships. Unity 6.7 builds with Gradle 9.1 / AGP 9.0, while the plugin still emits
/// the project the way AGP 3.6 wanted it:
///
///   - jcenter() was removed from Gradle 9.
///   - The androidlib declares its own AGP 3.6 classpath, clashing with the root.
///   - compileSdkVersion / targetSdkVersion were removed for library modules.
///   - buildToolsVersion pins 30.0.3, which is not installed with this editor.
///   - The manifest still carries package=, replaced by namespace in AGP 8.
///
/// On top of that the EOS aar needs Java 8+ core library desugaring, which AGP
/// verifies in every module consuming it. The plugin documents adding this through
/// custom launcher/main Gradle templates, but those templates would have to be kept
/// in sync with Unity's own by hand; patching the generated project instead tracks
/// whatever Unity emits.
///
/// This runs after Unity generates the Gradle project and before Gradle is invoked,
/// so it survives the plugin re-copying its files on every build and any re-resolve
/// of the package.
/// </summary>
public class EOSAndroidGradlePatcher : IPostGenerateGradleAndroidProject
{
    private const string LibraryName = "eos_dependencies.androidlib";

    /// <summary>Latest release on Google's Maven, which AGP 9 accepts.</summary>
    private const string DesugarLibrary = "com.android.tools:desugar_jdk_libs:2.1.5";

    private const string FallbackNamespace = "com.pew.eos_dependencies";

    public int callbackOrder => 0;

    /// <param name="path">The generated unityLibrary module directory.</param>
    public void OnPostGenerateGradleAndroidProject(string path)
    {
        PatchAndroidLibrary(Path.Combine(path, LibraryName));

        // unityLibrary pulls in eos-sdk.aar directly and launcher inherits it, so
        // AGP demands desugaring in both.
        EnableCoreLibraryDesugaring(Path.Combine(path, "build.gradle"));

        string gradleRoot = Path.GetDirectoryName(path);
        if (gradleRoot != null)
        {
            EnableCoreLibraryDesugaring(Path.Combine(gradleRoot, "launcher", "build.gradle"));
        }
    }

    private static void PatchAndroidLibrary(string libraryPath)
    {
        if (!Directory.Exists(libraryPath))
        {
            return;
        }

        // The namespace has to be read out of the manifest before it is stripped.
        string namespaceName = MoveManifestPackageToNamespace(Path.Combine(libraryPath, "AndroidManifest.xml"));

        string buildGradlePath = Path.Combine(libraryPath, "build.gradle");
        if (!File.Exists(buildGradlePath))
        {
            return;
        }

        string source = File.ReadAllText(buildGradlePath);
        string patched = source;

        // The root project already puts AGP on the classpath, so the library's own
        // buildscript block is both redundant and the thing that references jcenter.
        patched = RemoveBlock(patched, "buildscript");

        // compileSdkVersion is gone; AGP 9 wants the nested form Unity itself emits.
        Match compileSdk = Regex.Match(patched, @"compileSdkVersion\s+(\d+)");
        if (compileSdk.Success)
        {
            patched = patched.Remove(compileSdk.Index, compileSdk.Length)
                .Insert(compileSdk.Index, $"compileSdk {{\n        version = release({compileSdk.Groups[1].Value})\n    }}");
        }

        // Library modules no longer declare a target SDK, and the pinned build tools
        // version is not shipped with this editor.
        patched = Regex.Replace(patched, @"[ \t]*targetSdkVersion[ \t]+\d+[ \t]*\r?\n", string.Empty);
        patched = Regex.Replace(patched, @"[ \t]*buildToolsVersion[ \t]+'[^']*'[ \t]*\r?\n", string.Empty);

        // Anything left over from an older plugin build still needs the repository fix.
        patched = patched.Replace("jcenter()", "mavenCentral()");

        if (!Regex.IsMatch(patched, @"\bnamespace\s"))
        {
            Match androidBlock = Regex.Match(patched, @"android\s*\{");
            if (androidBlock.Success)
            {
                patched = patched.Insert(
                    androidBlock.Index + androidBlock.Length,
                    $"\n    namespace '{namespaceName}'");
            }
        }

        if (patched == source)
        {
            return;
        }

        File.WriteAllText(buildGradlePath, patched);
        Debug.Log($"EOS: rewrote {LibraryName}/build.gradle for AGP 9 (namespace '{namespaceName}').");
    }

    /// <summary>
    /// Adds the desugaring switch and the runtime it needs. Without this AGP fails
    /// checkReleaseAarMetadata with "Dependency ':eos-sdk:' requires core library
    /// desugaring to be enabled".
    /// </summary>
    private static void EnableCoreLibraryDesugaring(string buildGradlePath)
    {
        if (!File.Exists(buildGradlePath))
        {
            return;
        }

        string source = File.ReadAllText(buildGradlePath);
        string patched = source;

        if (!patched.Contains("coreLibraryDesugaringEnabled"))
        {
            Match compileOptions = Regex.Match(patched, @"compileOptions\s*\{");
            if (compileOptions.Success)
            {
                patched = patched.Insert(
                    compileOptions.Index + compileOptions.Length,
                    "\n        coreLibraryDesugaringEnabled true");
            }
        }

        if (!patched.Contains("coreLibraryDesugaring '"))
        {
            Match dependencies = Regex.Match(patched, @"(^|\n)dependencies\s*\{");
            if (dependencies.Success)
            {
                patched = patched.Insert(
                    dependencies.Index + dependencies.Length,
                    $"\n    coreLibraryDesugaring '{DesugarLibrary}'");
            }
        }

        if (patched == source)
        {
            return;
        }

        File.WriteAllText(buildGradlePath, patched);

        string moduleName = Path.GetFileName(Path.GetDirectoryName(buildGradlePath));
        Debug.Log($"EOS: enabled core library desugaring in {moduleName}/build.gradle.");
    }

    /// <summary>
    /// Removes the package attribute AGP 8 dropped and returns its value so it can
    /// be re-declared as the Gradle namespace.
    /// </summary>
    private static string MoveManifestPackageToNamespace(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return FallbackNamespace;
        }

        string manifest = File.ReadAllText(manifestPath);
        Match match = Regex.Match(manifest, "package\\s*=\\s*\"([^\"]+)\"");

        if (!match.Success)
        {
            return FallbackNamespace;
        }

        File.WriteAllText(manifestPath, manifest.Remove(match.Index, match.Length));
        return match.Groups[1].Value;
    }

    /// <summary>
    /// Deletes a named top-level Groovy block, matching braces so nested ones in the
    /// block body do not end it early.
    /// </summary>
    private static string RemoveBlock(string source, string blockName)
    {
        Match header = Regex.Match(source, blockName + @"\s*\{");
        if (!header.Success)
        {
            return source;
        }

        int depth = 0;
        for (int i = header.Index + header.Length - 1; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Remove(header.Index, i - header.Index + 1).TrimStart();
                }
            }
        }

        // Unbalanced braces - leave the file alone rather than corrupting it.
        return source;
    }
}
#endif
