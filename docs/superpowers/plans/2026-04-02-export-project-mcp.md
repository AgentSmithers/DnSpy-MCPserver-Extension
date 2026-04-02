# Export_Project MCP Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an `Export_Project` MCP command that exports a loaded dnSpy assembly to a Visual Studio project/solution by reusing dnSpy's native export pipeline through reflection.

**Architecture:** Keep the feature in the extension layer. Broaden MCP command discovery so the server can find commands across the extension assembly, then add a new `ExportCommands.cs` command that resolves modules/decompilers through public dnSpy contracts and calls the internal `dnSpy.Decompiler.MSBuild` export types via reflection. Preserve native export behavior for assembly-load suppression, project-capable language validation, and optional BAML-to-XAML export when the services are available.

**Tech Stack:** C#, .NET Framework 4.8, dnSpy extension/MEF imports, Model Context Protocol over SSE, dnlib, reflection over internal `dnSpy.Decompiler.MSBuild` types.

---

## File Map

- Modify: `Extensions/Examples/Example1.Extension/MCPServer.cs`
Purpose: widen command discovery to scan the extension assembly, and store new globally accessible services required by export.

- Modify: `Extensions/Examples/Example1.Extension/TheExtension.cs`
Purpose: import and store decompiler/BAML services in `Global`, and construct `SimpleMcpServer` using assembly-wide command discovery.

- Create: `Extensions/Examples/Example1.Extension/ExportCommands.cs`
Purpose: implement `Export_Project`, module resolution, decompiler resolution, reflection helpers, and export result formatting.

- Modify: `README.md`
Purpose: document the new command in the supported command list and explain its major arguments.

- Verify: `Extensions/Examples/Example1.Extension/Example1.Extension.csproj`
Purpose: confirm no new compile-time dependency on `dnSpy.Decompiler` is required. The reflection-based design should avoid project-reference churn.

## Baseline Notes

- The extension project does not currently build in a fresh checkout until prerequisite outputs exist for `dnSpy.Roslyn` and `ICSharpCode.TreeView`.
- Before making code changes, build the prerequisite projects so later compile failures are attributable to the new work.
- Do not change `dnSpy.Decompiler` visibility or add `InternalsVisibleTo` just for this feature. The plan intentionally avoids modifying dnSpy core assembly boundaries.

### Task 1: Restore a Useful Build Baseline

**Files:**
- Verify: `dnSpy.sln`
- Verify: `Libraries/ICSharpCode.TreeView/ICSharpCode.TreeView.csproj`
- Verify: `dnSpy/dnSpy/dnSpy.csproj`
- Verify: `Extensions/Examples/Example1.Extension/Example1.Extension.csproj`

- [ ] **Step 1: Build the tree-view dependency that the sample extension references directly**

Run:

```powershell
dotnet build "Libraries/ICSharpCode.TreeView/ICSharpCode.TreeView.csproj" -nologo
```

Expected: build succeeds and writes `Libraries/ICSharpCode.TreeView/bin/Debug/net48/ICSharpCode.TreeView.dll`.

- [ ] **Step 2: Build dnSpy so the extension's `dnSpy.Roslyn` hint path exists**

Run:

```powershell
dotnet build "dnSpy/dnSpy/dnSpy.csproj" -nologo
```

Expected: build succeeds and writes `dnSpy/dnSpy/bin/Debug/net48/dnspy.exe` plus `dnSpy.Roslyn.dll`.

- [ ] **Step 3: Build the sample extension and record the true pre-change baseline**

Run:

```powershell
dotnet build "Extensions/Examples/Example1.Extension/Example1.Extension.csproj" -nologo
```

Expected: the project either succeeds or fails only with pre-existing warnings/errors unrelated to `Export_Project`. Save the exact output in the work log before moving on.

- [ ] **Step 4: If the extension still fails before any code changes, stop and capture the failure in the implementation notes**

Record the failure text exactly, for example:

```text
warning MSB3245: Could not resolve this reference. Could not locate the assembly "dnSpy.Roslyn".
warning MSB3245: Could not resolve this reference. Could not locate the assembly "ICSharpCode.TreeView".
error CS0246: The type or namespace name 'ICSharpCode' could not be found.
```

Expected: a clear before/after baseline exists for later verification.

### Task 2: Broaden MCP Command Discovery and Global Service Access

**Files:**
- Modify: `Extensions/Examples/Example1.Extension/MCPServer.cs`
- Modify: `Extensions/Examples/Example1.Extension/TheExtension.cs`
- Verify: `Extensions/Examples/Example1.Extension/MCPCommands.cs`

- [ ] **Step 1: Extend `Global` so export code can access the decompiler and optional XAML/BAML services**

Update the `Global` class in `MCPServer.cs` to include the new shared services:

```csharp
static class Global {
	public static SimpleMcpServer MySimpleMCPServer;
	public static IDocumentTreeView MyTreeView;
	public static dnSpy.Contracts.App.IAppWindow MyAppWindow;
	public static IDocumentTabService MyDocumentTabService;
	public static dnSpy.Contracts.Decompiler.IDecompilerService MyDecompilerService;
	public static dnSpy.Contracts.Decompiler.IBamlDecompiler? MyBamlDecompiler;
	public static dnSpy.Contracts.Decompiler.IXamlOutputOptionsProvider? MyXamlOutputOptionsProvider;
}
```

Expected: export logic can read required services without re-plumbing every command method signature.

- [ ] **Step 2: Change `SimpleMcpServer` to scan the extension assembly instead of one command type**

Replace the constructor registration loop in `MCPServer.cs` with assembly-wide discovery:

```csharp
public SimpleMcpServer() {
	string IPAddress = "+";
	string port = "3003";
	Console.WriteLine("MCP server listening on " + IPAddress + ":" + port);

	_listener.Prefixes.Add("http://" + IPAddress + ":" + port + "/sse/");
	_listener.Prefixes.Add("http://" + IPAddress + ":" + port + "/message/");

	foreach (var type in typeof(SimpleMcpServer).Assembly.GetTypes()) {
		foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
			var attr = method.GetCustomAttribute<CommandAttribute>();
			if (attr != null)
				_commands[attr.Name] = method;
		}
	}
}
```

Expected: a new `ExportCommands.cs` file will be auto-discovered without modifying command registration again.

- [ ] **Step 3: Import and store the optional export-related services in `TheExtension.cs`**

Add imports alongside the existing decompiler import:

```csharp
[ImportMany] public IEnumerable<Lazy<IBamlDecompiler>> BamlDecompilers { get; set; }
[ImportMany] public IEnumerable<Lazy<IXamlOutputOptionsProvider>> XamlOutputOptionsProviders { get; set; }
```

Then store them at startup:

```csharp
Global.MyDecompilerService = decompilerService;
Global.MyBamlDecompiler = BamlDecompilers?.FirstOrDefault()?.Value;
Global.MyXamlOutputOptionsProvider = XamlOutputOptionsProviders?.FirstOrDefault()?.Value;
```

Expected: the export command can preserve dnSpy's `DecompileXaml` behavior when those services are present.

- [ ] **Step 4: Construct the server with the new no-argument constructor**

Update the server startup line in `TheExtension.cs`:

```csharp
Global.MySimpleMCPServer = new SimpleMcpServer();
```

Remove the temporary `MCPCommands MyMCPCommands = new MCPCommands();` construction if it becomes unused.

Expected: server startup still succeeds and all existing commands remain available.

- [ ] **Step 5: Build the extension to verify command-discovery refactoring did not break compilation**

Run:

```powershell
dotnet build "Extensions/Examples/Example1.Extension/Example1.Extension.csproj" -nologo
```

Expected: success, or only the same pre-existing baseline issues captured in Task 1.

- [ ] **Step 6: Commit the command-discovery and service-plumbing changes**

Run:

```powershell
git add "Extensions/Examples/Example1.Extension/MCPServer.cs" "Extensions/Examples/Example1.Extension/TheExtension.cs"
git commit -m "prepare extension for assembly-wide MCP commands"
```

Expected: one focused commit containing only server-registration and service-plumbing changes.

### Task 3: Implement the Reflection-Based Export Adapter

**Files:**
- Create: `Extensions/Examples/Example1.Extension/ExportCommands.cs`
- Verify: `dnSpy/dnSpy/Documents/Tabs/SaveCommands.cs`
- Verify: `dnSpy/dnSpy/MainApp/App.xaml.cs`
- Verify: `dnSpy/dnSpy.Contracts.Logic/Decompiler/IDecompiler.cs`

- [ ] **Step 1: Create the new command file with a command class and the public MCP entrypoint**

Start the file with the same command attribute style used by `MCPCommands.cs`:

```csharp
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using dnlib.DotNet;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.TreeView;
using static Example1.Extension.SimpleMcpServer;

namespace Example1.Extension {
	static class ExportCommands {
		[Command("Export_Project", MCPCmdDescription = "Exports a loaded assembly to a Visual Studio project or solution using dnSpy's native export pipeline.")]
		public static string Export_Project(string assemblyName, string outputDirectory, string decompiler = "", string projectVersion = "VS2022", bool sdkStyle = true, bool createSolution = true, string solutionFilename = "", bool unpackResources = true, bool createResX = true, bool decompileXaml = true) {
			return Global.MyAppWindow.MainWindow.Dispatcher.Invoke(() =>
				Export_Project_Impl(assemblyName, outputDirectory, decompiler, projectVersion, sdkStyle, createSolution, solutionFilename, unpackResources, createResX, decompileXaml)
			) as string ?? "Error: Export did not return a result";
		}
	}
}
```

Expected: the MCP server exposes one new command without changing existing command files.

- [ ] **Step 2: Add module and decompiler resolution helpers that use public dnSpy contracts only**

Implement helper methods that match dnSpy's language resolution rules and filter to project-capable decompilers:

```csharp
static ModuleDef[] ResolveModules(string assemblyName) =>
	Global.MyTreeView
		.GetAllModuleNodes()
		.Select(n => n.GetModule())
		.Where(m => m.Assembly is not null && string.Equals(m.Assembly.Name, assemblyName, StringComparison.OrdinalIgnoreCase))
		.ToArray();

static IDecompiler ResolveDecompiler(string decompilerName) {
	var candidates = Global.MyDecompilerService.AllDecompilers
		.Where(d => d.ProjectFileExtension is not null)
		.ToArray();

	if (candidates.Length == 0)
		throw new InvalidOperationException("No project-capable decompilers are available.");

	if (string.IsNullOrWhiteSpace(decompilerName)) {
		return Global.MyDecompilerService.Decompiler.ProjectFileExtension is not null
			? Global.MyDecompilerService.Decompiler
			: candidates[0];
	}

	if (Guid.TryParse(decompilerName, out var guid)) {
		var byGuid = Global.MyDecompilerService.Find(guid);
		if (byGuid is not null && byGuid.ProjectFileExtension is not null)
			return byGuid;
	}

	var byName = candidates.FirstOrDefault(d =>
		string.Equals(d.UniqueNameUI, decompilerName, StringComparison.OrdinalIgnoreCase) ||
		string.Equals(d.GenericNameUI, decompilerName, StringComparison.OrdinalIgnoreCase) ||
		string.Equals(d.GenericNameUI.Replace("#", "Sharp"), decompilerName, StringComparison.OrdinalIgnoreCase) ||
		string.Equals("VB", decompilerName, StringComparison.OrdinalIgnoreCase));

	if (byName is null)
		throw new InvalidOperationException("Decompiler not found: " + decompilerName);

	return byName;
}
```

Expected: callers can pass `"C#"`, `"Visual Basic"`, `"IL"`, `"VB"`, or a decompiler GUID.

- [ ] **Step 3: Add reflection helpers for the internal MSBuild export types**

Implement helpers that load the `dnSpy.Decompiler` assembly and set internal properties/fields safely:

```csharp
static Assembly GetDecompilerAssembly() {
	return AppDomain.CurrentDomain.GetAssemblies()
		.FirstOrDefault(a => string.Equals(a.GetName().Name, "dnSpy.Decompiler", StringComparison.Ordinal))
		?? throw new InvalidOperationException("dnSpy.Decompiler assembly is not loaded.");
}

static Type GetRequiredType(Assembly assembly, string fullName) {
	return assembly.GetType(fullName, throwOnError: false)
		?? throw new InvalidOperationException("Required type not found: " + fullName);
}

static object CreateNonPublic(Type type, params object[] args) {
	return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null)
		?? throw new InvalidOperationException("Failed to create instance of " + type.FullName);
}

static void SetProperty(object instance, string name, object? value) {
	var prop = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	if (prop is null)
		throw new InvalidOperationException("Property not found: " + name);
	prop.SetValue(instance, value);
}

static void SetField(object instance, string name, object? value) {
	var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	if (field is null)
		throw new InvalidOperationException("Field not found: " + name);
	field.SetValue(instance, value);
}
```

Expected: the command can instantiate and configure internal export types without adding `InternalsVisibleTo` or publicizing dnSpy assemblies.

- [ ] **Step 4: Implement `Export_Project_Impl` using dnSpy's native export rules**

Add the core export method. Match native behavior for assembly-load suppression and method-body decompilation:

```csharp
static string Export_Project_Impl(string assemblyName, string outputDirectory, string decompilerName, string projectVersionName, bool sdkStyle, bool createSolution, string solutionFilename, bool unpackResources, bool createResX, bool decompileXaml) {
	if (string.IsNullOrWhiteSpace(assemblyName))
		return "Error: assemblyName is required";
	if (string.IsNullOrWhiteSpace(outputDirectory))
		return "Error: outputDirectory is required";

	Directory.CreateDirectory(outputDirectory);

	var modules = ResolveModules(assemblyName);
	if (modules.Length == 0)
		return $"Error: Assembly '{assemblyName}' not found in loaded assemblies";

	var decompiler = ResolveDecompiler(decompilerName);
	if (decompiler.ProjectFileExtension is null)
		return $"Error: Decompiler '{decompiler.UniqueNameUI}' does not support project export";

	var decompilationContext = new DecompilationContext {
		CancellationToken = CancellationToken.None,
		GetDisableAssemblyLoad = () => Global.MyTreeView.DocumentService.DisableAssemblyLoad(),
		AsyncMethodBodyDecompilation = false,
	};

	var asm = GetDecompilerAssembly();
	var optionsType = GetRequiredType(asm, "dnSpy.Decompiler.MSBuild.ProjectCreatorOptions");
	var moduleOptionsType = GetRequiredType(asm, "dnSpy.Decompiler.MSBuild.ProjectModuleOptions");
	var creatorType = GetRequiredType(asm, "dnSpy.Decompiler.MSBuild.MSBuildProjectCreator");
	var projectVersionType = GetRequiredType(asm, "dnSpy.Decompiler.MSBuild.ProjectVersion");

	var options = CreateNonPublic(optionsType, outputDirectory, CancellationToken.None);
	SetProperty(options, "ProjectVersion", Enum.Parse(projectVersionType, projectVersionName, ignoreCase: true));
	SetProperty(options, "GenerateSDKStyleProjects", sdkStyle);
	if (createSolution)
		SetProperty(options, "SolutionFilename", NormalizeSolutionFilename(solutionFilename, assemblyName));

	var projectModules = (IList)optionsType.GetProperty("ProjectModules")!.GetValue(options)!;
	foreach (var module in modules) {
		var moduleOptions = CreateNonPublic(moduleOptionsType, module, decompiler, decompilationContext);
		SetProperty(moduleOptions, "UnpackResources", unpackResources);
		SetProperty(moduleOptions, "CreateResX", createResX);
		SetProperty(moduleOptions, "DecompileXaml", decompileXaml);
		if (Global.MyBamlDecompiler is not null) {
			Func<ModuleDef, byte[], CancellationToken, Stream, System.Collections.Generic.IList<string>> decompileBaml =
				(a, b, c, d) => Global.MyBamlDecompiler.Decompile(a, b, c, BamlDecompilerOptions.Create(decompiler), d, Global.MyXamlOutputOptionsProvider?.Default ?? new XamlOutputOptions());
			SetField(moduleOptions, "DecompileBaml", decompileBaml);
		}
		projectModules.Add(moduleOptions);
	}

	var creator = CreateNonPublic(creatorType, options);
	creatorType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(creator, null);

	return BuildSuccessMessage(creator, creatorType, assemblyName, outputDirectory, createSolution);
}
```

Expected: export behavior mirrors native dnSpy logic closely enough to produce full projects without copying the MSBuild implementation into the extension.

- [ ] **Step 5: Add result-formatting helpers that report output paths instead of guessed type counts**

Use the internal creator's public getters where available:

```csharp
static string BuildSuccessMessage(object creator, Type creatorType, string assemblyName, string outputDirectory, bool createSolution) {
	var sb = new StringBuilder();
	var projectFilenames = ((System.Collections.Generic.IEnumerable<string>)creatorType.GetProperty("ProjectFilenames")!.GetValue(creator)!).ToArray();

	sb.AppendLine("Export completed successfully.");
	sb.AppendLine("Assembly: " + assemblyName);
	sb.AppendLine("Output: " + outputDirectory);
	if (createSolution) {
		var solutionFilename = (string)creatorType.GetProperty("SolutionFilename")!.GetValue(creator)!;
		sb.AppendLine("Solution: " + solutionFilename);
	}
	sb.AppendLine("Projects:");
	foreach (var project in projectFilenames)
		sb.AppendLine("  " + project);
	return sb.ToString().TrimEnd();
}
```

Expected: results are deterministic and avoid promising counts that the native pipeline does not expose directly.

- [ ] **Step 6: Build the extension after adding `ExportCommands.cs`**

Run:

```powershell
dotnet build "Extensions/Examples/Example1.Extension/Example1.Extension.csproj" -nologo
```

Expected: success, or only known baseline issues unrelated to the new file.

- [ ] **Step 7: Commit the new export command implementation**

Run:

```powershell
git add "Extensions/Examples/Example1.Extension/ExportCommands.cs"
git commit -m "add reflection-based project export command"
```

Expected: one focused commit containing the new command file.

### Task 4: Document and Harden the Export Command Surface

**Files:**
- Modify: `README.md`
- Modify: `Extensions/Examples/Example1.Extension/ExportCommands.cs`

- [ ] **Step 1: Add README documentation for the new MCP command**

Update the supported commands section in `README.md` to add the new line:

```text
Export_Project - Exports a loaded assembly to a Visual Studio project or solution using dnSpy's native export pipeline
```

Then add one short argument example near the command list:

```text
Example: Export_Project("MyAssembly", "C:\\Exports\\MyAssembly", "C#", "VS2022", true, true, "MyAssembly.sln", true, true, true)
```

Expected: users understand that this is a full project export, not a single-file source dump.

- [ ] **Step 2: Harden error messages in `ExportCommands.cs` for the failure modes identified during analysis**

Add or update error paths to return explicit messages for:

```csharp
catch (TargetInvocationException ex) {
	return "Error: Export failed: " + (ex.InnerException?.Message ?? ex.Message);
}
catch (ArgumentException ex) {
	return "Error: Invalid export argument: " + ex.Message;
}
catch (Exception ex) {
	return "Error: Export failed: " + ex.Message;
}
```

Also improve the decompiler-not-found branch:

```csharp
var available = string.Join(", ", Global.MyDecompilerService.AllDecompilers
	.Where(d => d.ProjectFileExtension is not null)
	.Select(d => d.UniqueNameUI));
return "Error: Decompiler not found. Available export-capable decompilers: " + available;
```

Expected: MCP callers get actionable failures instead of reflection stack traces.

- [ ] **Step 3: Build again after documentation and error handling updates**

Run:

```powershell
dotnet build "Extensions/Examples/Example1.Extension/Example1.Extension.csproj" -nologo
```

Expected: no new compiler errors.

- [ ] **Step 4: Commit the documentation and error-handling polish**

Run:

```powershell
git add "README.md" "Extensions/Examples/Example1.Extension/ExportCommands.cs"
git commit -m "document project export MCP command"
```

Expected: README and command hardening land together in one follow-up commit.

### Task 5: Verify the Command End-to-End Through MCP

**Files:**
- Verify: `Extensions/Examples/Example1.Extension/ExportCommands.cs`
- Verify: generated export directory outside the repo, e.g. `C:\Temp\DnSpyExportSmoke`

- [ ] **Step 1: Launch dnSpy with the extension loaded**

Run:

```powershell
Resolve-Path ".\dnSpy\dnSpy\bin\Debug\net48\dnspy.exe"
.\dnSpy\dnSpy\bin\Debug\net48\dnspy.exe --extension-directory ".\Extensions\Examples\Example1.Extension\bin\Debug\net48"
```

Expected: dnSpy starts, shows the startup message box, and begins listening on port `3003` after clicking `OK`.

- [ ] **Step 2: Open an SSE session and capture the generated message endpoint**

Run in PowerShell:

```powershell
$sseLog = Join-Path $env:TEMP "dnspy-mcp-sse.txt"
Remove-Item $sseLog -ErrorAction Ignore
$sseProc = Start-Process curl.exe -ArgumentList '-N', 'http://127.0.0.1:3003/sse/' -RedirectStandardOutput $sseLog -PassThru
Start-Sleep -Seconds 2
Get-Content $sseLog
```

Expected output in the log file:

```text
event: endpoint
data: /message?sessionId=...
```

Keep `$sseProc` running for the next steps.

- [ ] **Step 3: Initialize the MCP session and list tools**

Run:

```powershell
$sessionPath = ((Get-Content $sseLog | Select-String 'data:\s+/message\?sessionId=' | Select-Object -First 1).ToString().Split('data: ')[1]).Trim()
curl.exe -X POST ("http://127.0.0.1:3003" + $sessionPath) -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"manual-smoke-test","version":"1.0.0"}}}'
curl.exe -X POST ("http://127.0.0.1:3003" + $sessionPath) -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
```

Expected: the SSE terminal prints responses containing `Export_Project` in the tool list.

- [ ] **Step 4: Call `Export_Project` against a small loaded assembly and verify files are written**

Run:

```powershell
curl.exe -X POST ("http://127.0.0.1:3003" + $sessionPath) -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"Export_Project","arguments":{"assemblyName":"System.Runtime","outputDirectory":"C:\\Temp\\DnSpyExportSmoke","decompiler":"C#","projectVersion":"VS2022","sdkStyle":true,"createSolution":true,"solutionFilename":"System.Runtime.sln","unpackResources":true,"createResX":true,"decompileXaml":true}}}'
```

Expected: the SSE terminal prints a success payload containing the output directory and one or more generated project paths.

- [ ] **Step 5: Verify the export directory contents on disk**

Run:

```powershell
Get-ChildItem "C:\Temp\DnSpyExportSmoke" -Recurse | Select-Object FullName
```

Expected: the directory contains `System.Runtime.sln`, one or more `.csproj` files, and decompiled source files.

- [ ] **Step 5a: Stop the temporary SSE client after verification**

Run:

```powershell
Stop-Process -Id $sseProc.Id
```

Expected: the background `curl.exe` SSE process exits cleanly.

- [ ] **Step 6: Commit any final fixes needed to make the smoke test pass cleanly**

Run:

```powershell
git add -A
git commit -m "fix export project command smoke test issues"
```

Expected: final code state passes the manual MCP export smoke test.

## Self-Review Checklist

- Spec coverage: the plan covers command discovery, global service plumbing, reflection over internal export types, decompiler resolution, README updates, and end-to-end MCP verification.
- Placeholder scan: fixed the MCP verification flow so the session id is extracted into `$sessionPath` automatically instead of relying on manual replacement.
- Type consistency: the plan consistently uses `Export_Project`, `Global.MyDecompilerService`, `Global.MyBamlDecompiler`, `Global.MyXamlOutputOptionsProvider`, and the internal type names under `dnSpy.Decompiler.MSBuild`.
